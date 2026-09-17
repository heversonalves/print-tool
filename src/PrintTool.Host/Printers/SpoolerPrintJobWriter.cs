using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PrintTool.Host.Printers.Native;

namespace PrintTool.Host.Printers;

[SupportedOSPlatform("windows")]
internal sealed class SpoolerPrintJobWriter : IPrintJobWriter
{
    private readonly IntPtr _printerHandle;
    private bool _finished;

    public SpoolerPrintJobWriter(IntPtr printerHandle)
    {
        _printerHandle = printerHandle;
    }

    public long BytesWritten { get; private set; }

    public Task WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFinished();

        byte[] buffer = chunk.ToArray();
        if (!WinSpool.WritePrinter(_printerHandle, buffer, buffer.Length, out int written))
        {
            throw new PrinterSpoolException($"WritePrinter falhou (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        BytesWritten += written;
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfFinished();
        _finished = true;

        try
        {
            if (!WinSpool.EndPagePrinter(_printerHandle))
            {
                throw new PrinterSpoolException($"EndPagePrinter falhou (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            if (!WinSpool.EndDocPrinter(_printerHandle))
            {
                throw new PrinterSpoolException($"EndDocPrinter falhou (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            WinSpool.ClosePrinter(_printerHandle);
        }

        return Task.CompletedTask;
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        if (_finished)
        {
            return Task.CompletedTask;
        }
        _finished = true;

        WinSpool.AbortPrinter(_printerHandle);
        WinSpool.ClosePrinter(_printerHandle);
        return Task.CompletedTask;
    }

    private void ThrowIfFinished()
    {
        if (_finished)
        {
            throw new InvalidOperationException("O job já foi concluído ou abortado.");
        }
    }
}
