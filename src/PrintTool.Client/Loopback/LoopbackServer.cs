using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintTool.Client.Discovery;
using PrintTool.Client.Forwarding;

namespace PrintTool.Client.Loopback;

/// <summary>
/// Sobe um <see cref="PrinterBridge"/> (listener 127.0.0.1:porta) para cada impressora
/// configurada em <see cref="ClientPrinterMappingConfig"/>.
/// </summary>
public sealed class LoopbackServer : IHostedService
{
    private readonly ClientPrinterMappingConfig _mappingConfig;
    private readonly DiscoveredHostTable _hostTable;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _spoolDirectory;

    private readonly List<PrinterBridge> _bridges = new();

    public LoopbackServer(
        ClientPrinterMappingConfig mappingConfig,
        DiscoveredHostTable hostTable,
        ILoggerFactory loggerFactory,
        string spoolDirectory)
    {
        _mappingConfig = mappingConfig;
        _hostTable = hostTable;
        _loggerFactory = loggerFactory;
        _spoolDirectory = spoolDirectory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (ClientPrinterMapping mapping in _mappingConfig.Mappings)
        {
            var forwarder = new JobForwarder(mapping.RemotePrinterName, _hostTable, _loggerFactory.CreateLogger<JobForwarder>());
            string queueDirectory = Path.Combine(_spoolDirectory, "queue", SanitizeForPath(mapping.RemotePrinterName));
            var queue = new LocalJobQueue(
                mapping.RemotePrinterName,
                forwarder,
                queueDirectory,
                _loggerFactory.CreateLogger($"LocalJobQueue[{mapping.RemotePrinterName}]"));

            var bridge = new PrinterBridge(
                mapping.LocalPort,
                mapping.RemotePrinterName,
                forwarder,
                queue,
                _spoolDirectory,
                _loggerFactory.CreateLogger($"PrinterBridge[{mapping.RemotePrinterName}]"));

            await bridge.StartAsync(cancellationToken).ConfigureAwait(false);
            _bridges.Add(bridge);
        }
    }

    private static string SanitizeForPath(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }
        return value;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (PrinterBridge bridge in _bridges)
        {
            await bridge.DisposeAsync().ConfigureAwait(false);
        }
        _bridges.Clear();
    }
}
