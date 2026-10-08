namespace PrintTool.Common.Protocol;

/// <summary>
/// Um frame de controle já lido da rede: versão do protocolo, tipo da mensagem
/// e o payload em JSON (UTF-8) correspondente ao tipo.
/// </summary>
public sealed record MessageEnvelope(byte Version, MessageType Type, byte[] Payload);
