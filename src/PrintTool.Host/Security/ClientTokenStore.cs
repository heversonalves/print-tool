using System.Security.Cryptography;
using System.Text.Json;

namespace PrintTool.Host.Security;

/// <summary>
/// Clients pareados com este Host e os tokens de longa duração emitidos para cada um.
/// Só o hash SHA-256 do token é persistido — o valor em claro existe apenas no instante da
/// emissão (devolvido uma única vez em <see cref="IssueToken"/>, dentro do <c>PairingResult</c>)
/// e nunca é gravado em disco, pelo mesmo motivo que uma senha nunca é gravada em claro.
/// Quando carregado via <see cref="LoadOrCreate"/>, observa o próprio arquivo e recarrega
/// sozinho — assim uma revogação feita pelo app de administração (processo separado do
/// serviço Windows, que só carregou o arquivo uma vez na inicialização) tem efeito imediato,
/// sem precisar reiniciar o serviço.
/// </summary>
public sealed class ClientTokenStore
{
    private const int TokenSizeBytes = 32; // 256 bits.

    private readonly string _path;
    private readonly object _lock = new();
    private List<ClientTokenEntry> _entries;
    private FileSystemWatcher? _watcher;

    private ClientTokenStore(string path, List<ClientTokenEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static ClientTokenStore LoadOrCreate(string path)
    {
        ClientTokenStore store;
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            List<ClientTokenEntry>? entries = JsonSerializer.Deserialize<List<ClientTokenEntry>>(json);
            store = new ClientTokenStore(path, entries ?? new List<ClientTokenEntry>());
        }
        else
        {
            store = new ClientTokenStore(path, new List<ClientTokenEntry>());
            store.Save();
        }

        store.AttachWatcher();
        return store;
    }

    /// <summary>
    /// Emite um novo token para <paramref name="clientId"/>, substituindo qualquer token
    /// anterior daquele mesmo cliente (repareamento). Devolve o token em claro — é a única
    /// vez que ele existe fora da memória do Client.
    /// </summary>
    public string IssueToken(Guid clientId, string displayName)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        string token = Convert.ToBase64String(tokenBytes);
        string tokenHash = HashToken(token);

        lock (_lock)
        {
            _entries.RemoveAll(e => e.ClientId == clientId);
            _entries.Add(new ClientTokenEntry(clientId, displayName, tokenHash, DateTimeOffset.UtcNow, Revoked: false));
            Save();
        }

        return token;
    }

    public bool Validate(Guid clientId, string presentedToken)
    {
        string presentedHash = HashToken(presentedToken);

        lock (_lock)
        {
            ClientTokenEntry? entry = _entries.FirstOrDefault(e => e.ClientId == clientId);
            if (entry is null || entry.Revoked)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(entry.TokenHash),
                Convert.FromBase64String(presentedHash));
        }
    }

    /// <summary>Revoga o acesso de um cliente já pareado. A próxima impressão dele exige novo pareamento via TOTP.</summary>
    public bool Revoke(Guid clientId)
    {
        lock (_lock)
        {
            int index = _entries.FindIndex(e => e.ClientId == clientId);
            if (index < 0)
            {
                return false;
            }

            _entries[index] = _entries[index] with { Revoked = true };
            Save();
            return true;
        }
    }

    public IReadOnlyList<ClientTokenEntry> ListClients()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private void Save()
    {
        string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.WriteAllText(_path, json);
    }

    private void AttachWatcher()
    {
        string fullPath = Path.GetFullPath(_path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => TryReload();
        watcher.Created += (_, _) => TryReload();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void TryReload()
    {
        // O arquivo pode estar sendo escrito ainda quando o evento dispara (inclusive por nós
        // mesmos, via Save()); algumas tentativas curtas bastam — se todas falharem, o próximo
        // evento de mudança tenta de novo. A leitura acontece DENTRO do lock — não só a
        // atribuição — porque ClientTokenStore é o único destes stores que também escreve a
        // partir da própria instância (IssueToken/Revoke): ler fora do lock permite que uma
        // leitura iniciada antes de um IssueToken/Revoke termine DEPOIS dele e sobrescreva o
        // estado novo com um conteúdo já ultrapassado.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                lock (_lock)
                {
                    string json = File.ReadAllText(_path);
                    List<ClientTokenEntry>? reloaded = JsonSerializer.Deserialize<List<ClientTokenEntry>>(json);
                    if (reloaded is not null)
                    {
                        _entries = reloaded;
                    }
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (JsonException)
            {
                Thread.Sleep(50);
            }
        }
    }
}

public sealed record ClientTokenEntry(Guid ClientId, string DisplayName, string TokenHash, DateTimeOffset PairedAtUtc, bool Revoked);
