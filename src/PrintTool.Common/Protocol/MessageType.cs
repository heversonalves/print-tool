namespace PrintTool.Common.Protocol;

/// <summary>
/// Tipo de cada frame de controle trocado entre Host e Client.
/// Os valores são estáveis: novos tipos só são adicionados ao final,
/// nunca reaproveitados, para preservar compatibilidade entre versões do protocolo.
/// </summary>
public enum MessageType : byte
{
    PrintJobRequestHeader = 1,
    PrintJobResult = 2,
    DiscoveryProbe = 3,
    DiscoveryAnnouncement = 4,
}
