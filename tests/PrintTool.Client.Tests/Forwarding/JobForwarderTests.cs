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

public class JobForwarderTests
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
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
    public async Task SendJobAsync_HostAvailable_DeliversJobAndReturnsResult()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[] jobData = { 1, 2, 3, 4, 5 };
        var serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync();
            NetworkStream stream = accepted.GetStream();
            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

            using var received = new MemoryStream();
            await FrameReader.ReadRawAsync(stream, header.DataLength, (chunk, _) => { received.Write(chunk.Span); return Task.CompletedTask; });

            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, received.Length));
            return received.ToArray();
        });

        DiscoveredHostTable table = await BuildResolvedTableAsync(port, "EPSON L3250");
        var forwarder = new JobForwarder("EPSON L3250", table, NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(jobData);
        PrintJobResult result = await forwarder.SendJobAsync("nota.pdf", source, jobData.Length, CancellationToken.None);

        byte[] receivedByServer = await serverTask;
        Assert.True(result.Success);
        Assert.Equal(jobData.Length, result.BytesWritten);
        Assert.Equal(jobData, receivedByServer);

        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task SendJobAsync_FirstConnectionDropped_RetriesAndSucceeds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using (TcpClient first = await listener.AcceptTcpClientAsync())
            {
                // Simula o Host caindo no meio da conexão, sem responder nada.
            }

            using TcpClient second = await listener.AcceptTcpClientAsync();
            NetworkStream stream = second.GetStream();
            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);
            await FrameReader.ReadRawAsync(stream, header.DataLength, (_, _) => Task.CompletedTask);
            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, header.DataLength));
        });

        DiscoveredHostTable table = await BuildResolvedTableAsync(port, "EPSON L3250");
        var forwarder = new JobForwarder("EPSON L3250", table, NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(new byte[] { 9, 9, 9 });
        PrintJobResult result = await forwarder.SendJobAsync("teste.txt", source, 3, CancellationToken.None);

        await serverTask;
        Assert.True(result.Success);

        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task SendJobAsync_PrinterNeverAnnounced_ThrowsAfterRetries()
    {
        var stub = new StubDiscoveryClient(); // nunca resolve nada
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        var forwarder = new JobForwarder("Impressora Fantasma", table, NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<IOException>(() => forwarder.SendJobAsync("x.txt", source, 1, CancellationToken.None));

        await forwarder.DisposeAsync();
    }
}
