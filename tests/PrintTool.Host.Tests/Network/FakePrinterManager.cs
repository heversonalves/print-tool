using PrintTool.Host.Printers;

namespace PrintTool.Host.Tests.Network;

/// <summary>
/// Substitui a implementação real de spooler nos testes de <see cref="PrintTool.Host.Network.PrintServer"/>,
/// que não dependem de Windows nem de impressora física.
/// </summary>
internal sealed class FakePrinterManager : IPrinterManager
{
    private readonly IReadOnlyList<string> _localPrinterNames;

    public FakePrinterManager(IReadOnlyList<string>? localPrinterNames = null)
    {
        _localPrinterNames = localPrinterNames ?? Array.Empty<string>();
    }

    public List<(string PrinterName, string JobName, byte[] Data, bool Aborted)> Jobs { get; } = new();

    public IReadOnlyList<string> GetLocalPrinterNames() => _localPrinterNames;

    public Task<IPrintJobWriter> BeginJobAsync(string printerName, string jobName, CancellationToken cancellationToken)
    {
        IPrintJobWriter writer = new FakeJobWriter(this, printerName, jobName);
        return Task.FromResult(writer);
    }

    private sealed class FakeJobWriter : IPrintJobWriter
    {
        private readonly FakePrinterManager _owner;
        private readonly string _printerName;
        private readonly string _jobName;
        private readonly MemoryStream _buffer = new();
        private bool _finished;

        public FakeJobWriter(FakePrinterManager owner, string printerName, string jobName)
        {
            _owner = owner;
            _printerName = printerName;
            _jobName = jobName;
        }

        public long BytesWritten => _buffer.Length;

        public Task WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
        {
            _buffer.Write(chunk.Span);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            _finished = true;
            _owner.Jobs.Add((_printerName, _jobName, _buffer.ToArray(), Aborted: false));
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            if (_finished)
            {
                return Task.CompletedTask;
            }
            _finished = true;
            _owner.Jobs.Add((_printerName, _jobName, _buffer.ToArray(), Aborted: true));
            return Task.CompletedTask;
        }
    }
}
