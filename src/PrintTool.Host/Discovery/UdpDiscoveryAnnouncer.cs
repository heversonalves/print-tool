using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;
using PrintTool.Host.Security;

namespace PrintTool.Host.Discovery;

/// <summary>
/// Responde, por UDP, às sondagens de descoberta enviadas pelos Clients: "quem tem impressoras?".
/// Cada resposta é enviada por unicast direto ao Client que perguntou, com hostname, IP,
/// porta TCP do <see cref="PrintServer"/> e a lista de impressoras compartilhadas.
/// </summary>
public sealed class UdpDiscoveryAnnouncer : IDiscoveryAnnouncer, IHostedService
{
    private readonly IPrinterManager _printerManager;
    private readonly SharedPrintersConfig _sharedPrinters;
    private readonly HostIdentity _hostIdentity;
    private readonly int _tcpPort;
    private readonly int _udpPort;
    private readonly ILogger<UdpDiscoveryAnnouncer> _logger;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _stoppingCts;
    private Task? _receiveLoopTask;

    public UdpDiscoveryAnnouncer(
        IPrinterManager printerManager,
        SharedPrintersConfig sharedPrinters,
        HostIdentity hostIdentity,
        IOptions<PrintServerOptions> printServerOptions,
        IOptions<DiscoveryOptions> discoveryOptions,
        ILogger<UdpDiscoveryAnnouncer> logger)
    {
        _printerManager = printerManager;
        _sharedPrinters = sharedPrinters;
        _hostIdentity = hostIdentity;
        _tcpPort = printServerOptions.Value.Port;
        _udpPort = discoveryOptions.Value.UdpPort;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

        _logger.LogInformation("DiscoveryAnnouncer escutando sondagens UDP na porta {Port}.", _udpPort);
        LogLocalPrinters();
        _receiveLoopTask = ReceiveLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts?.Cancel();
        _udpClient?.Dispose();

        if (_receiveLoopTask is not null)
        {
            await Task.WhenAny(_receiveLoopTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udpClient!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            await HandleDatagramAsync(received, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleDatagramAsync(UdpReceiveResult received, CancellationToken cancellationToken)
    {
        if (!DiscoveryDatagramSerializer.TryDeserialize(received.Buffer, out DiscoveryDatagram? datagram) ||
            datagram!.Kind != DiscoveryDatagramKind.Probe)
        {
            return;
        }

        DiscoveryProbe probe;
        try
        {
            probe = DiscoveryDatagramSerializer.ReadMessage<DiscoveryProbe>(datagram);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sondagem de descoberta malformada recebida de {Remote}.", received.RemoteEndPoint);
            return;
        }

        DiscoveryAnnouncement announcement = BuildAnnouncement(probe.RequestId);
        byte[] response = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Announcement, announcement);

        try
        {
            await _udpClient!.SendAsync(response, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao responder sondagem de descoberta para {Remote}.", received.RemoteEndPoint);
        }
    }

    /// <summary>
    /// Registra na inicialização as impressoras locais vistas pelo spooler e quais estão
    /// compartilhadas, para o administrador conferir os nomes exatos a usar em sharedprinters.json.
    /// </summary>
    private void LogLocalPrinters()
    {
        IReadOnlyList<string> localPrinters;
        try
        {
            localPrinters = _printerManager.GetLocalPrinterNames();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao listar as impressoras locais.");
            return;
        }

        _logger.LogInformation("{Count} impressora(s) local(is) detectada(s).", localPrinters.Count);
        foreach (string printer in localPrinters)
        {
            _logger.LogInformation("  [{Status}] {Printer}", _sharedPrinters.IsShared(printer) ? "compartilhada" : "não compartilhada", printer);
        }
    }

    private DiscoveryAnnouncement BuildAnnouncement(Guid requestId)
    {
        IReadOnlyList<string> sharedPrinters = _printerManager.GetLocalPrinterNames()
            .Where(_sharedPrinters.IsShared)
            .ToList();

        return new DiscoveryAnnouncement(
            requestId,
            Environment.MachineName,
            LocalNetworkAddress.GetPrimaryIPv4().ToString(),
            _tcpPort,
            sharedPrinters,
            _hostIdentity.HostId,
            _hostIdentity.CertificateThumbprint);
    }
}
