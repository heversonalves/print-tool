using System.Text.Json;

namespace PrintTool.Client;

/// <summary>
/// Configuração simples, em arquivo JSON local, de quais portas locais o Client escuta e
/// para qual impressora remota cada uma encaminha. Cada entrada corresponde a uma impressora
/// cadastrada no Windows com "Porta TCP/IP Padrão" apontando para 127.0.0.1:LocalPort.
/// </summary>
public sealed class ClientPrinterMappingConfig
{
    public List<ClientPrinterMapping> Mappings { get; init; } = new();

    public static ClientPrinterMappingConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var empty = new ClientPrinterMappingConfig();
            empty.Save(path);
            return empty;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ClientPrinterMappingConfig>(json) ?? new ClientPrinterMappingConfig();
    }

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json);
    }
}
