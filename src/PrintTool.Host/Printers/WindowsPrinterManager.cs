using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PrintTool.Host.Printers.Native;

namespace PrintTool.Host.Printers;

/// <summary>
/// Implementação real de <see cref="IPrinterManager"/> sobre a API de spooler do Windows
/// (winspool.drv). Só roda em Windows — o ponto de maior risco técnico do projeto,
/// por isso fica isolado atrás da interface e é o primeiro item a ser validado manualmente.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrinterManager : IPrinterManager
{
    public IReadOnlyList<string> GetLocalPrinterNames()
    {
        const int level = 4;

        WinSpool.EnumPrinters(WinSpool.PrinterEnumLocal, null, level, IntPtr.Zero, 0, out int bytesNeeded, out _);
        if (bytesNeeded <= 0)
        {
            return Array.Empty<string>();
        }

        IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
        try
        {
            if (!WinSpool.EnumPrinters(WinSpool.PrinterEnumLocal, null, level, buffer, bytesNeeded, out _, out int returned))
            {
                throw new PrinterSpoolException($"EnumPrinters falhou (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            var names = new List<string>(returned);
            int structSize = Marshal.SizeOf<WinSpool.PRINTER_INFO_4>();
            for (int i = 0; i < returned; i++)
            {
                IntPtr entryPtr = IntPtr.Add(buffer, i * structSize);
                var entry = Marshal.PtrToStructure<WinSpool.PRINTER_INFO_4>(entryPtr);
                string? name = Marshal.PtrToStringUni(entry.pPrinterName);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task<IPrintJobWriter> BeginJobAsync(string printerName, string jobName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!WinSpool.OpenPrinter(printerName, out IntPtr printerHandle, IntPtr.Zero))
        {
            throw new PrinterSpoolException($"Não foi possível abrir a impressora '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            var docInfo = new WinSpool.DOC_INFO_1W
            {
                pDocName = jobName,
                pOutputFile = null,
                pDatatype = "RAW",
            };

            int jobId = WinSpool.StartDocPrinter(printerHandle, 1, ref docInfo);
            if (jobId == 0)
            {
                throw new PrinterSpoolException($"StartDocPrinter falhou para '{printerName}' (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            if (!WinSpool.StartPagePrinter(printerHandle))
            {
                int error = Marshal.GetLastWin32Error();
                WinSpool.EndDocPrinter(printerHandle);
                throw new PrinterSpoolException($"StartPagePrinter falhou para '{printerName}' (Win32 error {error}).");
            }

            IPrintJobWriter writer = new SpoolerPrintJobWriter(printerHandle);
            return Task.FromResult(writer);
        }
        catch
        {
            WinSpool.ClosePrinter(printerHandle);
            throw;
        }
    }
}
