using PrintTool.Client.Printing;
using Xunit;

namespace PrintTool.Client.Tests.Printing;

public class LocalPrinterInstallerTests
{
    [Fact]
    public void PortNameFor_SanitizesSpacesAndInvalidCharacters()
    {
        string portName = LocalPrinterInstaller.PortNameFor("EPSON L3150 Series");

        Assert.Equal("PrintTool_EPSON_L3150_Series", portName);
    }

    [Fact]
    public void BuildInstallScript_IsIdempotent_ChecksBeforeCreatingPortAndPrinter()
    {
        string script = LocalPrinterInstaller.BuildInstallScript("EPSON L3150 Series", 54321, "EPSON L3150 Series");

        Assert.Contains("Get-PrinterPort -Name 'PrintTool_EPSON_L3150_Series'", script);
        Assert.Contains("Add-PrinterPort -Name 'PrintTool_EPSON_L3150_Series' -PrinterHostAddress '127.0.0.1' -PortNumber 54321", script);
        Assert.Contains("Get-Printer -Name 'EPSON L3150 Series'", script);
        Assert.Contains("Add-Printer -Name 'EPSON L3150 Series' -PortName 'PrintTool_EPSON_L3150_Series' -DriverName 'EPSON L3150 Series'", script);
    }

    [Fact]
    public void BuildInstallScript_EscapesSingleQuotesInNames()
    {
        string script = LocalPrinterInstaller.BuildInstallScript("O'Brien's Printer", 1, "Some Driver");

        Assert.Contains("O''Brien''s Printer", script);
        Assert.DoesNotContain("O'Brien's Printer", script);
    }

    [Fact]
    public void SuggestDriverFor_PrefersExactMatch()
    {
        var drivers = new[] { "EPSON L3150 Series", "HP LaserJet Professional M1132 MFP" };

        string? suggestion = LocalPrinterInstaller.SuggestDriverFor("EPSON L3150 Series", drivers);

        Assert.Equal("EPSON L3150 Series", suggestion);
    }

    [Fact]
    public void SuggestDriverFor_FallsBackToPartialMatch()
    {
        var drivers = new[] { "EPSON L3150 Series (copy 1)", "HP LaserJet Professional M1132 MFP" };

        string? suggestion = LocalPrinterInstaller.SuggestDriverFor("EPSON L3150 Series", drivers);

        Assert.Equal("EPSON L3150 Series (copy 1)", suggestion);
    }

    [Fact]
    public void SuggestDriverFor_ReturnsNull_WhenNothingMatches()
    {
        var drivers = new[] { "Brother DCP-L5512DN Printer" };

        string? suggestion = LocalPrinterInstaller.SuggestDriverFor("EPSON L3150 Series", drivers);

        Assert.Null(suggestion);
    }
}
