using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Client.Discovery;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Discovery;

public class UdpDiscoveryClientTests
{
    [Fact]
    public async Task ProbeAsync_HostResponds_ReturnsMatchingAnnouncement()
    {
        // Porta livre em vez da padrão, para não disputar a porta com outros processos de teste
        // (os assemblies de teste rodam em paralelo) ou com um agente real instalado na máquina.
        using var fakeHost = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int udpPort = ((IPEndPoint)fakeHost.Client.LocalEndPoint!).Port;

        var fakeHostTask = Task.Run(async () =>
        {
            UdpReceiveResult received = await fakeHost.ReceiveAsync();
            DiscoveryDatagramSerializer.TryDeserialize(received.Buffer, out DiscoveryDatagram? datagram);
            var probe = DiscoveryDatagramSerializer.ReadMessage<DiscoveryProbe>(datagram!);

            var announcement = new DiscoveryAnnouncement(probe.RequestId, "HOST-CAIXA-01", "10.0.0.5", 9100, new[] { "EPSON L3250" }, Guid.NewGuid(), "AA:BB:CC");
            byte[] response = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Announcement, announcement);
            await fakeHost.SendAsync(response, received.RemoteEndPoint);
        });

        var client = CreateClient(udpPort);
        IReadOnlyList<DiscoveryAnnouncement> results = await client.ProbeAsync(TimeSpan.FromSeconds(3), CancellationToken.None);

        await fakeHostTask;

        Assert.Contains(results, a => a.HostName == "HOST-CAIXA-01" && a.Printers.Contains("EPSON L3250"));
    }

    [Fact]
    public async Task ProbeAsync_NoHostResponds_ReturnsEmptyAfterTimeout()
    {
        var client = CreateClient(GetFreeUdpPort());

        IReadOnlyList<DiscoveryAnnouncement> results = await client.ProbeAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Empty(results);
    }

    private static UdpDiscoveryClient CreateClient(int udpPort) =>
        new(Options.Create(new DiscoveryOptions { UdpPort = udpPort }), NullLogger<UdpDiscoveryClient>.Instance);

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
