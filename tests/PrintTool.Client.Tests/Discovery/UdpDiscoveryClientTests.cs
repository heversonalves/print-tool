using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
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
        using var fakeHost = new UdpClient();
        fakeHost.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        fakeHost.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryConstants.UdpPort));

        var fakeHostTask = Task.Run(async () =>
        {
            UdpReceiveResult received = await fakeHost.ReceiveAsync();
            DiscoveryDatagramSerializer.TryDeserialize(received.Buffer, out DiscoveryDatagram? datagram);
            var probe = DiscoveryDatagramSerializer.ReadMessage<DiscoveryProbe>(datagram!);

            var announcement = new DiscoveryAnnouncement(probe.RequestId, "HOST-CAIXA-01", "10.0.0.5", 9100, new[] { "EPSON L3250" });
            byte[] response = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Announcement, announcement);
            await fakeHost.SendAsync(response, received.RemoteEndPoint);
        });

        var client = new UdpDiscoveryClient(NullLogger<UdpDiscoveryClient>.Instance);
        IReadOnlyList<DiscoveryAnnouncement> results = await client.ProbeAsync(TimeSpan.FromSeconds(3), CancellationToken.None);

        await fakeHostTask;

        Assert.Contains(results, a => a.HostName == "HOST-CAIXA-01" && a.Printers.Contains("EPSON L3250"));
    }

    [Fact]
    public async Task ProbeAsync_NoHostResponds_ReturnsEmptyAfterTimeout()
    {
        var client = new UdpDiscoveryClient(NullLogger<UdpDiscoveryClient>.Instance);

        IReadOnlyList<DiscoveryAnnouncement> results = await client.ProbeAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Empty(results);
    }
}
