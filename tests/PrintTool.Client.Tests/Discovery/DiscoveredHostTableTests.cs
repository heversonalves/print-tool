using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Discovery;

public class DiscoveredHostTableTests
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
    }

    [Fact]
    public async Task RefreshAsync_ThenTryResolve_FindsHostForKnownPrinter()
    {
        var stub = new StubDiscoveryClient
        {
            NextResult = new[]
            {
                new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-CAIXA-01", "10.0.0.5", 9100, new[] { "EPSON L3250", "Brother HL-1212W" }),
            },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);

        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(table.TryResolve("EPSON L3250", out ResolvedHost? host));
        Assert.Equal("HOST-CAIXA-01", host!.HostName);
        Assert.Equal(9100, host.TcpPort);
        Assert.Equal("10.0.0.5", host.Address.ToString());
    }

    [Fact]
    public async Task TryResolve_UnknownPrinter_ReturnsFalse()
    {
        var stub = new StubDiscoveryClient
        {
            NextResult = new[]
            {
                new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-CAIXA-01", "10.0.0.5", 9100, new[] { "EPSON L3250" }),
            },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(table.TryResolve("Impressora Inexistente", out _));
    }

    [Fact]
    public async Task RefreshAsync_CalledAgain_ReplacesStaleEntries()
    {
        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-A", "10.0.0.5", 9100, new[] { "EPSON L3250" }) },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(table.TryResolve("EPSON L3250", out _));

        stub.NextResult = Array.Empty<DiscoveryAnnouncement>();
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(table.TryResolve("EPSON L3250", out _));
    }
}
