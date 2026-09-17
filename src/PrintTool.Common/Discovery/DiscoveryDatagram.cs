namespace PrintTool.Common.Discovery;

/// <summary>
/// Resultado da decodificação de um datagrama UDP de descoberta: o tipo identificado
/// e o payload JSON já isolado, pronto para ser desserializado no DTO correspondente
/// (<see cref="Messages.DiscoveryProbe"/> ou <see cref="Messages.DiscoveryAnnouncement"/>).
/// </summary>
public sealed record DiscoveryDatagram(DiscoveryDatagramKind Kind, byte[] JsonPayload);
