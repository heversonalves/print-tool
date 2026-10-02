namespace PrintTool.Common.Discovery;

public sealed class DiscoveryOptions
{
    /// <summary>
    /// Porta UDP de descoberta. Host e Clients da mesma rede precisam usar o mesmo valor;
    /// só deve ser alterada se a porta padrão já estiver ocupada na rede.
    /// </summary>
    public int UdpPort { get; set; } = DiscoveryConstants.UdpPort;
}
