namespace PrintTool.Host.Printers;

public interface IPrinterManager
{
    /// <summary>
    /// Nomes de todas as impressoras instaladas localmente nesta máquina.
    /// </summary>
    IReadOnlyList<string> GetLocalPrinterNames();

    /// <summary>
    /// Abre um job na fila de impressão local (via API de spooler do Windows) e devolve
    /// um <see cref="IPrintJobWriter"/> para alimentá-lo em blocos, conforme os dados chegam
    /// da rede — sem precisar acumular o job inteiro em memória antes de começar a imprimir.
    /// </summary>
    Task<IPrintJobWriter> BeginJobAsync(string printerName, string jobName, CancellationToken cancellationToken);
}
