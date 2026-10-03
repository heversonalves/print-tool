using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Discovery;

public class DiscoveryRefreshServiceTests
{
    private sealed class CountingDiscoveryClient : IDiscoveryClient
    {
        private int _probeCount;

        public int ProbeCount => Volatile.Read(ref _probeCount);

        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _probeCount);
            return Task.FromResult(NextResult);
        }
    }

    private static ClientPrinterMappingConfig MappingFor(string printerName) => new()
    {
        Mappings = { new ClientPrinterMapping(9200, printerName) },
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task StartAsync_ResolvesPrinterWithoutAnyJob()
    {
        var discovery = new CountingDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-CAIXA-01", "10.0.0.5", 9100, new[] { "EPSON L3150 Series" }) },
        };
        var table = new DiscoveredHostTable(discovery, NullLogger<DiscoveredHostTable>.Instance);
        var service = new DiscoveryRefreshService(
            table, MappingFor("EPSON L3150 Series"), NullLogger<DiscoveryRefreshService>.Instance,
            refreshInterval: TimeSpan.FromHours(1));

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => table.TryResolve("EPSON L3150 Series", out _));
            Assert.Equal(1, discovery.ProbeCount);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProbesAgainAfterInterval()
    {
        var discovery = new CountingDiscoveryClient();
        var table = new DiscoveredHostTable(discovery, NullLogger<DiscoveredHostTable>.Instance);
        var service = new DiscoveryRefreshService(
            table, MappingFor("EPSON L3150 Series"), NullLogger<DiscoveryRefreshService>.Instance,
            refreshInterval: TimeSpan.FromMilliseconds(50));

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => discovery.ProbeCount >= 3);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
