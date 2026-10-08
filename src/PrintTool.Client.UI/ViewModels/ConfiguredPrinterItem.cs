namespace PrintTool.Client.UI.ViewModels;

/// <summary>Um mapeamento já salvo em <c>printers.json</c>, com o status de pareamento atual.</summary>
public sealed class ConfiguredPrinterItem
{
    public ConfiguredPrinterItem(string remotePrinterName, int localPort, bool isPaired, string hostStatusText)
    {
        RemotePrinterName = remotePrinterName;
        LocalPort = localPort;
        IsPaired = isPaired;
        HostStatusText = hostStatusText;
    }

    public string RemotePrinterName { get; }

    public int LocalPort { get; }

    public bool IsPaired { get; }

    public string HostStatusText { get; }

    public string StatusText => IsPaired ? "Pareado" : "Pendente";

    public string PairButtonText => IsPaired ? "Parear novamente" : "Parear";
}
