namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Resposta do Host após processar (ou falhar ao processar) um job de impressão.
/// </summary>
public sealed record PrintJobResult(bool Success, long BytesWritten, string? ErrorMessage = null);
