using System.Text.Json;

namespace PrintTool.Host.Printers;

/// <summary>
/// Configuração simples, em arquivo JSON local, de quais impressoras instaladas na máquina
/// ficam expostas na rede. Substituído pelo console de gestão na Fase 4 — por ora, arquivo editável.
/// </summary>
public sealed class SharedPrintersConfig
{
    public List<string> SharedPrinterNames { get; init; } = new();

    /// <summary>
    /// Carrega a configuração de <paramref name="path"/>. Se o arquivo não existir,
    /// cria um novo vazio nesse caminho e o devolve, para que o administrador o edite.
    /// </summary>
    public static SharedPrintersConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var empty = new SharedPrintersConfig();
            empty.Save(path);
            return empty;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SharedPrintersConfig>(json) ?? new SharedPrintersConfig();
    }

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json);
    }

    public bool IsShared(string printerName) => SharedPrinterNames.Contains(printerName, StringComparer.OrdinalIgnoreCase);
}
