namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Resposta do Host a um <see cref="PairingRequest"/>. Quando <see cref="Approved"/> é
/// verdadeiro, <see cref="Token"/> traz o token de longa duração recém-emitido (em claro,
/// só nesta resposta — o Host nunca o guarda, apenas o seu hash) e <see cref="HostId"/>
/// identifica o Host de forma estável, para o Client fixar junto do token.
/// </summary>
public sealed record PairingResult(bool Approved, Guid? HostId = null, string? Token = null, string? Reason = null);
