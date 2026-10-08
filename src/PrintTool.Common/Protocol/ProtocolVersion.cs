namespace PrintTool.Common.Protocol;

public static class ProtocolVersion
{
    /// <summary>
    /// Versão atual do protocolo. Cada frame carrega essa versão no cabeçalho,
    /// para permitir evolução (ex.: autenticação na Fase 2) sem quebrar clientes antigos.
    /// </summary>
    public const byte Current = 1;
}
