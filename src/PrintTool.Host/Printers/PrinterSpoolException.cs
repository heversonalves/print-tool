namespace PrintTool.Host.Printers;

public class PrinterSpoolException : Exception
{
    public PrinterSpoolException(string message) : base(message)
    {
    }

    public PrinterSpoolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
