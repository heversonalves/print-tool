using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrintTool.Client.Discovery;

/// <summary>
/// Mantém a <see cref="DiscoveredHostTable"/> atualizada em segundo plano: sonda a rede uma vez
/// na inicialização e depois a cada <c>refreshInterval</c>, para que o Client já saiba qual Host
/// atende cada impressora antes do primeiro job chegar. O <see cref="Forwarding.JobForwarder"/>
/// continua sondando por conta própria quando não consegue resolver uma impressora.
/// </summary>
public sealed class DiscoveryRefreshService : BackgroundService
{
    private readonly DiscoveredHostTable _hostTable;
    private readonly string _mappingConfigPath;
    private readonly ILogger<DiscoveryRefreshService> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _probeTimeout;

    private readonly Dictionary<string, ResolvedHost?> _lastResolved = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveryRefreshService(
        DiscoveredHostTable hostTable,
        string mappingConfigPath,
        ILogger<DiscoveryRefreshService> logger,
        TimeSpan? refreshInterval = null,
        TimeSpan? probeTimeout = null)
    {
        _hostTable = hostTable;
        _mappingConfigPath = mappingConfigPath;
        _logger = logger;
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(30);
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _hostTable.RefreshAsync(_probeTimeout, stoppingToken).ConfigureAwait(false);
                LogResolutionChanges();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha na sondagem periódica de descoberta; nova tentativa em {Interval}.", _refreshInterval);
            }

            try
            {
                await Task.Delay(_refreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Registra no log só quando a resolução de uma impressora configurada muda (encontrada,
    /// trocou de Host ou sumiu), para não repetir a mesma linha a cada sondagem. Recarrega o
    /// mapeamento do disco a cada ciclo — assim uma impressora pareada pelo app de administração
    /// enquanto o serviço já estava de pé também aparece aqui, sem precisar reiniciar o serviço.
    /// </summary>
    private void LogResolutionChanges()
    {
        ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);

        foreach (ClientPrinterMapping mapping in mappingConfig.Mappings)
        {
            _hostTable.TryResolve(mapping.RemotePrinterName, out ResolvedHost? current);
            bool seenBefore = _lastResolved.TryGetValue(mapping.RemotePrinterName, out ResolvedHost? previous);
            _lastResolved[mapping.RemotePrinterName] = current;

            if (seenBefore && Equals(previous, current))
            {
                continue;
            }

            if (current is not null)
            {
                _logger.LogInformation(
                    "Impressora '{PrinterName}' resolvida para o Host '{HostName}' ({Address}:{Port}).",
                    mapping.RemotePrinterName, current.HostName, current.Address, current.TcpPort);
            }
            else
            {
                _logger.LogWarning("Nenhum Host anunciando a impressora '{PrinterName}' foi encontrado na rede.", mapping.RemotePrinterName);
            }
        }
    }
}
