using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Client.Discovery;

/// <summary>
/// Sonda a rede local via UDP broadcast perguntando "quem tem impressoras?" e coleta,
/// dentro de uma janela de tempo, as respostas unicast de cada Host disponível.
/// </summary>
public sealed class UdpDiscoveryClient : IDiscoveryClient
{
    private readonly ILogger<UdpDiscoveryClient> _logger;

    public UdpDiscoveryClient(ILogger<UdpDiscoveryClient> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(0) { EnableBroadcast = true };

        var probe = new DiscoveryProbe(Guid.NewGuid());
        byte[] probeBytes = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Probe, probe);

        foreach (IPEndPoint broadcastEndpoint in GetBroadcastEndpoints())
        {
            try
            {
                await udpClient.SendAsync(probeBytes, broadcastEndpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao enviar sondagem de descoberta para {Endpoint}.", broadcastEndpoint);
            }
        }

        var announcements = new List<DiscoveryAnnouncement>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                UdpReceiveResult received = await udpClient.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                if (!DiscoveryDatagramSerializer.TryDeserialize(received.Buffer, out DiscoveryDatagram? datagram) ||
                    datagram!.Kind != DiscoveryDatagramKind.Announcement)
                {
                    continue;
                }

                try
                {
                    DiscoveryAnnouncement announcement = DiscoveryDatagramSerializer.ReadMessage<DiscoveryAnnouncement>(datagram);
                    if (announcement.RequestId == probe.RequestId)
                    {
                        announcements.Add(announcement);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Anúncio de descoberta malformado recebido de {Remote}.", received.RemoteEndPoint);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Janela de tempo esgotada: comportamento esperado, não é erro.
        }

        return announcements;
    }

    /// <summary>
    /// Broadcast "limitado" (255.255.255.255) como fallback, mais o broadcast dirigido
    /// de cada interface IPv4 ativa — mais confiável em redes com mais de um adaptador.
    /// </summary>
    private static IEnumerable<IPEndPoint> GetBroadcastEndpoints()
    {
        var endpoints = new List<IPEndPoint> { new(IPAddress.Broadcast, DiscoveryConstants.UdpPort) };

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation addressInfo in nic.GetIPProperties().UnicastAddresses)
            {
                if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                IPAddress broadcast = GetDirectedBroadcastAddress(addressInfo.Address, addressInfo.IPv4Mask);
                endpoints.Add(new IPEndPoint(broadcast, DiscoveryConstants.UdpPort));
            }
        }

        return endpoints.Distinct();
    }

    private static IPAddress GetDirectedBroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        byte[] broadcastBytes = new byte[addressBytes.Length];

        for (int i = 0; i < addressBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }
}
