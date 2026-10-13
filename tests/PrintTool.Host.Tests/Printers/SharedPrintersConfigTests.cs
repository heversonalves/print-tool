using PrintTool.Host.Printers;
using Xunit;

namespace PrintTool.Host.Tests.Printers;

public class SharedPrintersConfigTests : IDisposable
{
    private readonly string _tempDir;

    public SharedPrintersConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string ConfigPath => Path.Combine(_tempDir, "sharedprinters.json");

    [Fact]
    public void LoadOrCreate_FileMissing_CreatesEmptyConfigOnDisk()
    {
        var config = SharedPrintersConfig.LoadOrCreate(ConfigPath);

        Assert.Empty(config.SharedPrinterNames);
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSharedPrinterNames()
    {
        var original = new SharedPrintersConfig { SharedPrinterNames = { "EPSON L3250", "Brother HL-1212W" } };
        original.Save(ConfigPath);

        var loaded = SharedPrintersConfig.LoadOrCreate(ConfigPath);

        Assert.Equal(original.SharedPrinterNames, loaded.SharedPrinterNames);
    }

    [Theory]
    [InlineData("EPSON L3250", true)]
    [InlineData("epson l3250", true)]
    [InlineData("Impressora Inexistente", false)]
    public void IsShared_IsCaseInsensitive(string printerName, bool expected)
    {
        var config = new SharedPrintersConfig { SharedPrinterNames = { "EPSON L3250" } };

        Assert.Equal(expected, config.IsShared(printerName));
    }

    [Fact]
    public async Task LoadOrCreate_FileChangedExternally_IsSharedReflectsNewContentWithoutRecreatingInstance()
    {
        var original = new SharedPrintersConfig { SharedPrinterNames = { "EPSON L3250" } };
        original.Save(ConfigPath);

        SharedPrintersConfig config = SharedPrintersConfig.LoadOrCreate(ConfigPath);
        Assert.True(config.IsShared("EPSON L3250"));
        Assert.False(config.IsShared("Brother HL-1212W"));

        // Simula o app de administração trocando quais impressoras estão compartilhadas,
        // sem que o serviço (dono de 'config') seja reiniciado.
        var updated = new SharedPrintersConfig { SharedPrinterNames = { "Brother HL-1212W" } };
        updated.Save(ConfigPath);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !config.IsShared("Brother HL-1212W"))
        {
            await Task.Delay(100);
        }

        Assert.True(config.IsShared("Brother HL-1212W"));
        Assert.False(config.IsShared("EPSON L3250"));
    }
}
