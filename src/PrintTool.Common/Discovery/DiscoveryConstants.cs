namespace PrintTool.Common.Discovery;

public static class DiscoveryConstants
{
    /// <summary>
    /// Porta UDP usada para sondagem (broadcast, Client -> rede) e para resposta
    /// (unicast, Host -> Client). Fixa e conhecida por ambos os agentes.
    /// </summary>
    public const int UdpPort = 8721;

    /// <summary>
    /// Prefixo que identifica um datagrama como pertencente ao Print Tool, para descartar
    /// rapidamente qualquer outro tráfego de broadcast/multicast que chegue nessa porta.
    /// </summary>
    public static readonly byte[] Magic = { (byte)'P', (byte)'T', (byte)'D', (byte)'1' };
}
