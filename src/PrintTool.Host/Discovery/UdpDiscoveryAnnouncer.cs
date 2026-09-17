using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;

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
    private readonly int _tcpPort;
    private readonly ILogger<UdpDiscoveryAnnouncer> _logger;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _stoppingCts;
    private Task? _receiveLoopTask;

    public UdpDiscoveryAnnouncer(
        IPrinterManager printerManager,
        SharedPrintersConfig sharedPrinters,
        IOptions<PrintServerOptions> printServerOptions,
        ILogger<UdpDiscoveryAnnouncer> logger)
    {
        _printerManager = printerManager;
        _sharedPrinters = sharedPrinters;
        _tcpPort = printServerOptions.Value.Port;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryConstants.UdpPort));

        _logger.LogInformation("DiscoveryAnnouncer escutando sondagens UDP na porta {Port}.", DiscoveryConstants.UdpPort);
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
            sharedPrinters);
    }
}
