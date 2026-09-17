namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Resposta unicast enviada por um Host a uma <see cref="DiscoveryProbe"/>, identificando
/// como alcançá-lo e quais impressoras ele expõe na rede.
/// </summary>
public sealed record DiscoveryAnnouncement(
    Guid RequestId,
    string HostName,
    string IpAddress,
    int TcpPort,
    IReadOnlyList<string> Printers);
