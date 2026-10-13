namespace PrintTool.Client.UI.ViewModels;

/// <summary>Uma impressora compartilhada vista na rede durante a sondagem de descoberta.</summary>
public sealed class DiscoveredPrinterItem
{
    public DiscoveredPrinterItem(string printerName, string hostName, string address, bool isAlreadyConfigured)
    {
        PrinterName = printerName;
        HostName = hostName;
        Address = address;
        IsAlreadyConfigured = isAlreadyConfigured;
    }

    public string PrinterName { get; }

    public string HostName { get; }

    public string Address { get; }

    public bool IsAlreadyConfigured { get; }

    public string ConnectButtonText => IsAlreadyConfigured ? "Já configurada" : "Conectar";
}
