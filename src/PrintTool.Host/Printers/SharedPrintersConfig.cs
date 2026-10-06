using System.Text.Json;

namespace PrintTool.Host.Printers;

/// <summary>
/// Configuração simples, em arquivo JSON local, de quais impressoras instaladas na máquina
/// ficam expostas na rede. Quando carregada via <see cref="LoadOrCreate"/>, observa o próprio
/// arquivo e recarrega sozinha — assim o app de administração pode mudar quais impressoras
/// estão compartilhadas sem precisar reiniciar o serviço.
/// </summary>
public sealed class SharedPrintersConfig
{
    private readonly object _lock = new();
    private HashSet<string>? _reloaded;
    private FileSystemWatcher? _watcher;

    public List<string> SharedPrinterNames { get; init; } = new();

    /// <summary>
    /// Carrega a configuração de <paramref name="path"/>. Se o arquivo não existir,
    /// cria um novo vazio nesse caminho e o devolve, para que o administrador o edite.
    /// Passa a observar o arquivo: mudanças feitas por fora (ex. pelo app de administração)
    /// refletem em <see cref="IsShared"/> sem precisar recriar este objeto.
    /// </summary>
    public static SharedPrintersConfig LoadOrCreate(string path)
    {
        SharedPrintersConfig config;
        if (!File.Exists(path))
        {
            config = new SharedPrintersConfig();
            config.Save(path);
        }
        else
        {
            string json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<SharedPrintersConfig>(json) ?? new SharedPrintersConfig();
        }

        config.AttachWatcher(path);
        return config;
    }

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json);
    }

    public bool IsShared(string printerName)
    {
        lock (_lock)
        {
            if (_reloaded is not null)
            {
                return _reloaded.Contains(printerName);
            }
        }

        return SharedPrinterNames.Contains(printerName, StringComparer.OrdinalIgnoreCase);
    }

    private void AttachWatcher(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => TryReload(fullPath);
        watcher.Created += (_, _) => TryReload(fullPath);
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void TryReload(string path)
    {
        // O arquivo pode estar sendo escrito ainda quando o evento dispara; algumas tentativas
        // curtas bastam — se todas falharem, o próximo evento de mudança tenta de novo.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                string json = File.ReadAllText(path);
                SharedPrintersConfig? reloaded = JsonSerializer.Deserialize<SharedPrintersConfig>(json);
                if (reloaded is not null)
                {
                    lock (_lock)
                    {
                        _reloaded = new HashSet<string>(reloaded.SharedPrinterNames, StringComparer.OrdinalIgnoreCase);
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
