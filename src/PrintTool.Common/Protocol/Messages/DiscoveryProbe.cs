namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Sondagem UDP de broadcast enviada pelo Client perguntando "quem tem impressoras?".
/// </summary>
public sealed record DiscoveryProbe(Guid RequestId);
