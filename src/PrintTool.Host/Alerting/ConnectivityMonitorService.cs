using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintTool.Host.Security;

namespace PrintTool.Host.Alerting;

/// <summary>
/// Sonda periodicamente quais máquinas pareadas (e não revogadas) estão com uma conexão
/// autenticada ativa, e manda um alerta por e-mail só quando esse estado MUDA — igual ao
/// padrão já usado pelo <c>DiscoveryRefreshService</c> para a descoberta de impressoras: a
/// primeira observação de cada cliente só vira uma linha de base silenciosa, nunca um alerta.
/// Fora do horário comercial configurado (<see cref="AlertingOptions.BusinessHours"/>), a
/// checagem inteira é pulada — uma máquina desligada fora do expediente é esperado, não um
/// problema.
/// </summary>
public sealed class ConnectivityMonitorService : BackgroundService
{
    private readonly ClientTokenStore _clientTokenStore;
    private readonly ConnectivityRegistry _connectivityRegistry;
    private readonly AlertingOptions _options;
    private readonly IAlertSender _alertSender;
    private readonly ILogger<ConnectivityMonitorService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly Func<DateTimeOffset> _clock;

    private readonly Dictionary<Guid, bool> _lastKnownConnected = new();

    public ConnectivityMonitorService(
        ClientTokenStore clientTokenStore,
        ConnectivityRegistry connectivityRegistry,
        AlertingOptions options,
        IAlertSender alertSender,
        ILogger<ConnectivityMonitorService> logger,
        TimeSpan? checkInterval = null,
        Func<DateTimeOffset>? clock = null)
    {
        _clientTokenStore = clientTokenStore;
        _connectivityRegistry = connectivityRegistry;
        _options = options;
        _alertSender = alertSender;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(Math.Max(1, options.CheckIntervalMinutes));
        _clock = clock ?? (static () => DateTimeOffset.Now);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha na checagem periódica de conectividade; nova tentativa em {Interval}.", _checkInterval);
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock();
        if (!_options.IsWithinBusinessHours(now))
        {
            return;
        }

        foreach (ClientTokenEntry client in _clientTokenStore.ListClients())
        {
            if (client.Revoked)
            {
                // Revogado não é "desconectado" — é esperado não ter conexão. Remove do
                // acompanhamento para não alertar e para que um futuro repareamento comece
                // com uma linha de base nova, silenciosa.
                _lastKnownConnected.Remove(client.ClientId);
                continue;
            }

            bool currentlyConnected = _connectivityRegistry.IsConnected(client.ClientId);
            bool seenBefore = _lastKnownConnected.TryGetValue(client.ClientId, out bool previouslyConnected);
            _lastKnownConnected[client.ClientId] = currentlyConnected;

            if (!seenBefore || previouslyConnected == currentlyConnected)
            {
                continue;
            }

            if (currentlyConnected)
            {
                await _alertSender.SendAsync(
                    $"[PrintTool] Conexão restaurada: {client.DisplayName}",
                    $"A máquina '{client.DisplayName}' reconectou ao Host em {now:dd/MM/yyyy HH:mm}.",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _alertSender.SendAsync(
                    $"[PrintTool] Máquina desconectada: {client.DisplayName}",
                    $"A máquina '{client.DisplayName}' não está mais conectada ao Host (detectado em {now:dd/MM/yyyy HH:mm}).",
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
