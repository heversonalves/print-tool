using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintTool.Client.Discovery;
using PrintTool.Client.Forwarding;
using PrintTool.Client.Security;

namespace PrintTool.Client.Loopback;

/// <summary>
/// Sobe um <see cref="PrinterBridge"/> (listener 127.0.0.1:porta) para cada impressora
/// configurada em <see cref="ClientPrinterMappingConfig"/>, e observa o arquivo de
/// configuração para adicionar/remover bridges em tempo real — assim o app de administração
/// consegue conectar (ou remover) uma impressora sem precisar reiniciar o serviço.
/// </summary>
public sealed class LoopbackServer : IHostedService
{
    private readonly string _mappingConfigPath;
    private readonly DiscoveredHostTable _hostTable;
    private readonly ClientIdentity _clientIdentity;
    private readonly HostTokenStore _tokenStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LoopbackServer> _logger;
    private readonly string _spoolDirectory;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly Dictionary<int, (ClientPrinterMapping Mapping, PrinterBridge Bridge)> _bridges = new();

    private FileSystemWatcher? _watcher;
    private CancellationToken _lifetimeToken;

    public LoopbackServer(
        string mappingConfigPath,
        DiscoveredHostTable hostTable,
        ClientIdentity clientIdentity,
        HostTokenStore tokenStore,
        ILoggerFactory loggerFactory,
        string spoolDirectory)
    {
        _mappingConfigPath = mappingConfigPath;
        _hostTable = hostTable;
        _clientIdentity = clientIdentity;
        _tokenStore = tokenStore;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<LoopbackServer>();
        _spoolDirectory = spoolDirectory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetimeToken = cancellationToken;
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        AttachWatcher();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;

        await _reloadLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            foreach ((_, PrinterBridge bridge) in _bridges.Values)
            {
                await bridge.DisposeAsync().ConfigureAwait(false);
            }
            _bridges.Clear();
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void AttachWatcher()
    {
        string fullPath = Path.GetFullPath(_mappingConfigPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => _ = ReloadSafelyAsync();
        watcher.Created += (_, _) => _ = ReloadSafelyAsync();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private async Task ReloadSafelyAsync()
    {
        try
        {
            await ReloadAsync(_lifetimeToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao recarregar '{Path}'.", _mappingConfigPath);
        }
    }

    /// <summary>
    /// Relê <see cref="ClientPrinterMappingConfig"/> do disco e ajusta os bridges em execução
    /// para bater com o conteúdo atual: sobe os que faltam, derruba os que sumiram ou mudaram
    /// de impressora remota. Chamado na inicialização e sempre que o arquivo muda.
    /// </summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClientPrinterMappingConfig? config = await ReadConfigWithRetryAsync(cancellationToken).ConfigureAwait(false);
            if (config is null)
            {
                return;
            }

            var desired = new Dictionary<int, ClientPrinterMapping>();
            foreach (ClientPrinterMapping mapping in config.Mappings)
            {
                desired[mapping.LocalPort] = mapping;
            }

            foreach (int port in _bridges.Keys.ToList())
            {
                (ClientPrinterMapping existing, PrinterBridge bridge) = _bridges[port];
                bool stillWanted = desired.TryGetValue(port, out ClientPrinterMapping? current) &&
                    string.Equals(current.RemotePrinterName, existing.RemotePrinterName, StringComparison.Ordinal);

                if (!stillWanted)
                {
                    await bridge.DisposeAsync().ConfigureAwait(false);
                    _bridges.Remove(port);
                    _logger.LogInformation("Bridge da porta {Port} ('{PrinterName}') removido.", port, existing.RemotePrinterName);
                }
            }

            foreach (ClientPrinterMapping mapping in desired.Values)
            {
                if (_bridges.ContainsKey(mapping.LocalPort))
                {
                    continue;
                }

                PrinterBridge bridge = await StartBridgeAsync(mapping, cancellationToken).ConfigureAwait(false);
                _bridges[mapping.LocalPort] = (mapping, bridge);
                _logger.LogInformation("Bridge da porta {Port} ('{PrinterName}') adicionado.", mapping.LocalPort, mapping.RemotePrinterName);
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task<PrinterBridge> StartBridgeAsync(ClientPrinterMapping mapping, CancellationToken cancellationToken)
    {
        var forwarder = new JobForwarder(mapping.RemotePrinterName, _hostTable, _clientIdentity, _tokenStore, _loggerFactory.CreateLogger<JobForwarder>());
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
        return bridge;
    }

    private async Task<ClientPrinterMappingConfig?> ReadConfigWithRetryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static string SanitizeForPath(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }
        return value;
    }
}
