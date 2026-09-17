using System.Net;
using Microsoft.Extensions.Logging;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Client.Discovery;

/// <summary>
/// Mantém, em memória, a última tabela impressora → Host conhecida, atualizada por
/// <see cref="RefreshAsync"/>. Isola o <see cref="JobForwarder"/> (que só precisa
/// resolver um nome de impressora) dos detalhes de sondagem via <see cref="IDiscoveryClient"/>.
/// </summary>
public sealed class DiscoveredHostTable
{
    private readonly IDiscoveryClient _discoveryClient;
    private readonly ILogger<DiscoveredHostTable> _logger;
    private readonly object _lock = new();

    private Dictionary<string, ResolvedHost> _byPrinterName = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveredHostTable(IDiscoveryClient discoveryClient, ILogger<DiscoveredHostTable> logger)
    {
        _discoveryClient = discoveryClient;
        _logger = logger;
    }

    /// <summary>
    /// Sonda a rede e substitui a tabela conhecida pelo resultado mais recente.
    /// Chamado periodicamente e sempre que a conexão com o Host atual falhar.
    /// </summary>
    public async Task RefreshAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveryAnnouncement> announcements = await _discoveryClient.ProbeAsync(timeout, cancellationToken).ConfigureAwait(false);

        var updated = new Dictionary<string, ResolvedHost>(StringComparer.OrdinalIgnoreCase);
        foreach (DiscoveryAnnouncement announcement in announcements)
        {
            if (!IPAddress.TryParse(announcement.IpAddress, out IPAddress? address))
            {
                _logger.LogWarning("Anúncio de {HostName} com IP inválido: {IpAddress}.", announcement.HostName, announcement.IpAddress);
                continue;
            }

            var host = new ResolvedHost(announcement.HostName, address, announcement.TcpPort);
            foreach (string printerName in announcement.Printers)
            {
                updated[printerName] = host;
            }
        }

        lock (_lock)
        {
            _byPrinterName = updated;
        }

        _logger.LogInformation("Descoberta atualizada: {Count} impressora(s) resolvida(s) na rede.", updated.Count);
    }

    public bool TryResolve(string printerName, out ResolvedHost? host)
    {
        lock (_lock)
        {
            return _byPrinterName.TryGetValue(printerName, out host);
        }
    }
}
