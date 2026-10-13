namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Resposta do Host a um <see cref="AuthenticateRequest"/>. Quando <see cref="Success"/> é
/// falso, a conexão é encerrada pelo Host — nenhum job é aceito sem autenticação válida.
/// </summary>
public sealed record AuthenticateResult(bool Success, string? Reason = null);
