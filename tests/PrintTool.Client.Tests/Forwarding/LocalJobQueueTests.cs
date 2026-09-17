using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Client.Forwarding;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Forwarding;

public class LocalJobQueueTests : IDisposable
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
    }

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_queue_" + Guid.NewGuid());

    public LocalJobQueueTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<DiscoveredHostTable> BuildResolvedTableAsync(int port, string printerName)
    {
        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-TESTE", "127.0.0.1", port, new[] { printerName }) },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        return table;
    }

    [Fact]
    public async Task EnqueueAsync_HostUnavailable_JobStaysQueuedOnDisk()
    {
        int port = GetFreeTcpPort(); // ninguém escuta aqui: Host "fora do ar"
        DiscoveredHostTable table = await BuildResolvedTableAsync(port, "EPSON L3250");
        var forwarder = new JobForwarder("EPSON L3250", table, NullLogger<JobForwarder>.Instance);
        var queueDir = Path.Combine(_tempDir, "queue");
        var queue = new LocalJobQueue("EPSON L3250", forwarder, queueDir, NullLogger.Instance, drainInterval: TimeSpan.FromMilliseconds(300));
        queue.Start(CancellationToken.None);

        string sourceFile = Path.Combine(_tempDir, "job1.spool");
        await File.WriteAllBytesAsync(sourceFile, new byte[] { 1, 2, 3 });

        await queue.EnqueueAsync(sourceFile, "job1", CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(3)); // dá tempo para ao menos uma rodada de dreno (que deve falhar)

        Assert.Equal(2, Directory.GetFiles(queueDir).Length); // .data + .meta ainda presentes

        await queue.DisposeAsync();
        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueAsync_HostBecomesAvailableLater_JobIsEventuallyDelivered()
    {
        int port = GetFreeTcpPort();
        DiscoveredHostTable table = await BuildResolvedTableAsync(port, "EPSON L3250");
        var forwarder = new JobForwarder("EPSON L3250", table, NullLogger<JobForwarder>.Instance);
        var queueDir = Path.Combine(_tempDir, "queue");
        var queue = new LocalJobQueue("EPSON L3250", forwarder, queueDir, NullLogger.Instance, drainInterval: TimeSpan.FromMilliseconds(300));
        queue.Start(CancellationToken.None);

        byte[] jobBytes = { 10, 20, 30, 40 };
        string sourceFile = Path.Combine(_tempDir, "job1.spool");
        await File.WriteAllBytesAsync(sourceFile, jobBytes);
        await queue.EnqueueAsync(sourceFile, "job1", CancellationToken.None);

        // Host "volta": sobe o listener na mesma porta já resolvida pela DiscoveredHostTable.
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var receivedTcs = new TaskCompletionSource<byte[]>();
        _ = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync();
            NetworkStream stream = accepted.GetStream();
            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

            using var received = new MemoryStream();
            await FrameReader.ReadRawAsync(stream, header.DataLength, (chunk, _) => { received.Write(chunk.Span); return Task.CompletedTask; });
            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, received.Length));

            receivedTcs.SetResult(received.ToArray());
        });

        byte[] received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(jobBytes, received);

        // Aguarda o dreno remover os arquivos da fila (a resposta já chegou; falta o cleanup local).
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Directory.GetFiles(queueDir).Length > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
        Assert.Empty(Directory.GetFiles(queueDir));

        await queue.DisposeAsync();
        await forwarder.DisposeAsync();
    }
}
