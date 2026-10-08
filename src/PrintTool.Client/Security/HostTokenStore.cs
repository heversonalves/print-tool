using System.Text.Json;

namespace PrintTool.Client.Security;

/// <summary>
/// O token de longa duração emitido no pareamento com um Host específico, mais o thumbprint
/// do certificado TLS daquele Host fixado nesse momento (TOFU — <c>trust on first use</c>).
/// Conexões futuras validam o certificado apresentado contra <see cref="CertThumbprint"/>,
/// não contra o que a descoberta UDP anunciar naquele instante.
/// </summary>
public sealed record HostTokenEntry(Guid HostId, string HostDisplayName, string CertThumbprint, string Token, DateTimeOffset PairedAtUtc);

/// <summary>
/// Tokens de pareamento desta máquina, um por Host, indexados por <c>HostId</c>. Recarregado
/// do disco a cada acesso — é um arquivo pequeno, e isso evita ficar com uma cópia em memória
/// desatualizada depois de um <c>pair</c> rodado em outro processo (CLI) enquanto o serviço já estava de pé.
/// </summary>
public sealed class HostTokenStore
{
    private readonly string _path;

    public HostTokenStore(string path)
    {
        _path = path;
    }

    public HostTokenEntry? TryGet(Guid hostId) => Load().FirstOrDefault(e => e.HostId == hostId);

    public void Save(HostTokenEntry entry)
    {
        List<HostTokenEntry> entries = Load().Where(e => e.HostId != entry.HostId).ToList();
        entries.Add(entry);
        Persist(entries);
    }

    private List<HostTokenEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return new List<HostTokenEntry>();
        }

        string json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<HostTokenEntry>>(json) ?? new List<HostTokenEntry>();
    }

    private void Persist(List<HostTokenEntry> entries)
    {
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.WriteAllText(_path, json);
    }
}
