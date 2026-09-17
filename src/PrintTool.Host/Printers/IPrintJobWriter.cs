namespace PrintTool.Host.Printers;

/// <summary>
/// Um job de impressão aberto no spooler, aguardando os blocos de dados.
/// Chamar exatamente um de <see cref="CompleteAsync"/> ou <see cref="AbortAsync"/> ao final.
/// </summary>
public interface IPrintJobWriter
{
    long BytesWritten { get; }

    Task WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken);

    Task CompleteAsync(CancellationToken cancellationToken);

    Task AbortAsync(CancellationToken cancellationToken);
}
