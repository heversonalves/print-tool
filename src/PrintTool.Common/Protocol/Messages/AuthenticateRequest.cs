namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Primeiro frame de uma conexão já pareada: apresenta o token de longa duração emitido
/// no pareamento. Precede qualquer <see cref="PrintJobRequestHeader"/> na mesma conexão.
/// </summary>
public sealed record AuthenticateRequest(Guid ClientId, string Token);
