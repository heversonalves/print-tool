namespace PrintTool.Host.Network;

public sealed class PrintServerOptions
{
    /// <summary>
    /// Porta TCP em que o Host escuta pedidos de impressão vindos dos Clients.
    /// </summary>
    public int Port { get; set; } = 9100;
}
