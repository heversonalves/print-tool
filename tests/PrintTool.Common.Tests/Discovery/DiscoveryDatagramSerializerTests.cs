using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Common.Tests.Discovery;

public class DiscoveryDatagramSerializerTests
{
    [Fact]
    public void SerializeThenDeserialize_Probe_RoundTrips()
    {
        var probe = new DiscoveryProbe(Guid.NewGuid());
        byte[] bytes = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Probe, probe);

        bool ok = DiscoveryDatagramSerializer.TryDeserialize(bytes, out DiscoveryDatagram? datagram);

        Assert.True(ok);
        Assert.Equal(DiscoveryDatagramKind.Probe, datagram!.Kind);
        Assert.Equal(probe, DiscoveryDatagramSerializer.ReadMessage<DiscoveryProbe>(datagram));
    }

    [Fact]
    public void SerializeThenDeserialize_Announcement_RoundTrips()
    {
        var announcement = new DiscoveryAnnouncement(
            Guid.NewGuid(),
            "HOST-CAIXA-01",
            "192.168.0.42",
            9100,
            new[] { "EPSON L3250", "Brother HL-1212W" });

        byte[] bytes = DiscoveryDatagramSerializer.Serialize(DiscoveryDatagramKind.Announcement, announcement);
        bool ok = DiscoveryDatagramSerializer.TryDeserialize(bytes, out DiscoveryDatagram? datagram);

        Assert.True(ok);
        Assert.Equal(DiscoveryDatagramKind.Announcement, datagram!.Kind);

        var decoded = DiscoveryDatagramSerializer.ReadMessage<DiscoveryAnnouncement>(datagram);
        Assert.Equal(announcement.RequestId, decoded.RequestId);
        Assert.Equal(announcement.HostName, decoded.HostName);
        Assert.Equal(announcement.IpAddress, decoded.IpAddress);
        Assert.Equal(announcement.TcpPort, decoded.TcpPort);
        Assert.Equal(announcement.Printers, decoded.Printers);
    }

    [Fact]
    public void TryDeserialize_ForeignTraffic_ReturnsFalse()
    {
        byte[] randomBytes = { 1, 2, 3, 4, 5, 6, 7, 8 };

        bool ok = DiscoveryDatagramSerializer.TryDeserialize(randomBytes, out DiscoveryDatagram? datagram);

        Assert.False(ok);
        Assert.Null(datagram);
    }

    [Fact]
    public void TryDeserialize_TooShort_ReturnsFalse()
    {
        bool ok = DiscoveryDatagramSerializer.TryDeserialize(new byte[] { 1, 2 }, out DiscoveryDatagram? datagram);

        Assert.False(ok);
        Assert.Null(datagram);
    }
}
