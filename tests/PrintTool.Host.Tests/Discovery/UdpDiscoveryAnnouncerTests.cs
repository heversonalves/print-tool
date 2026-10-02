using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Host.Discovery;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;
using PrintTool.Host.Tests.Network;
using Xunit;

namespace PrintTool.Host.Tests.Discovery;

public class UdpDiscoveryAnnouncerTests : IAsyncLifetime
{
    private readonly FakePrinterManager _printerManager = new(new[] { "EPSON L3250", "Brother HL-1212W" });
    private readonly SharedPrintersConfig _sharedPrinters = new() { SharedPrinterNames = { "EPSON L3250" } };
    // Porta livre em vez da padrão, para não disputar a porta com outros processos de teste
    // (os assemblies de teste rodam em paralelo) ou com um agente real instalado na máquina.
    private readonly int _udpPort = GetFreeUdpPort();
    private UdpDiscoveryAnnouncer _announcer = null!;

    public async Task InitializeAsync()
    {
        _announcer = new UdpDiscoveryAnnouncer(
            _printerManager,
            _sharedPrinters,
            Options.Create(new PrintServerOptions { Port = 9100 }),
            Options.Create(new DiscoveryOptions { UdpPort = _udpPort }),
            NullLogger<UdpDiscoveryAnnouncer>.Instance);

        await _announcer.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _announcer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Probe_GetsAnnouncementWithOnlySharedPrinters()
    {
        using var probeClient = new UdpClient(0);
        var probe = new DiscoveryProbe(Guid.NewGuid());
        byte[] probeBytes = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Probe, probe);
        await probeClient.SendAsync(probeBytes, new IPEndPoint(IPAddress.Loopback, _udpPort));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await probeClient.ReceiveAsync(cts.Token);

        bool ok = DiscoveryDatagramSerializer.TryDeserialize(received.Buffer, out DiscoveryDatagram? datagram);
        Assert.True(ok);
        Assert.Equal(DiscoveryDatagramKind.Announcement, datagram!.Kind);

        var announcement = DiscoveryDatagramSerializer.ReadMessage<DiscoveryAnnouncement>(datagram);
        Assert.Equal(probe.RequestId, announcement.RequestId);
        Assert.Equal(Environment.MachineName, announcement.HostName);
        Assert.Equal(9100, announcement.TcpPort);
        Assert.Equal(new[] { "EPSON L3250" }, announcement.Printers);
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
