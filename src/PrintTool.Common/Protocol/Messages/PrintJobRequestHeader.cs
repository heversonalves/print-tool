namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Primeiro frame de uma requisição de impressão. Os <see cref="DataLength"/> bytes brutos
/// do job seguem imediatamente depois deste frame, sem envelope adicional.
/// </summary>
public sealed record PrintJobRequestHeader(string PrinterName, string JobName, long DataLength);
