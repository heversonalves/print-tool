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
}
