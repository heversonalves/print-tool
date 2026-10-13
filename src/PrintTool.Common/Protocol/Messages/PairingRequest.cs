namespace PrintTool.Common.Protocol.Messages;

/// <summary>
/// Pedido de pareamento: uma máquina nova se apresenta ao Host com o código TOTP de 6 dígitos
/// que o administrador leu no app autenticador, pedindo a emissão de um token de longa duração.
/// </summary>
public sealed record PairingRequest(Guid ClientId, string ClientDisplayName, string Code);
