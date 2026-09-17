using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Client.Loopback;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Loopback;

public class LoopbackServerEndToEndTests
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task DriverWritesJob_FlowsThroughLoopbackAndForwarder_ReachesFakeHost()
    {
        // Fake Host: aceita a conexão do JobForwarder e responde como o PrintServer real responderia.
        using var hostListener = new TcpListener(IPAddress.Loopback, 0);
        hostListener.Start();
        int hostPort = ((IPEndPoint)hostListener.LocalEndpoint).Port;

        byte[] jobBytes = new byte[30_000];
        new Random(7).NextBytes(jobBytes);

        var hostReceivedPrinter = new TaskCompletionSource<(string PrinterName, byte[] Data)>();
        var hostTask = Task.Run(async () =>
        {
            using TcpClient accepted = await hostListener.AcceptTcpClientAsync();
            NetworkStream stream = accepted.GetStream();
            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

            using var received = new MemoryStream();
            await FrameReader.ReadRawAsync(stream, header.DataLength, (chunk, _) => { received.Write(chunk.Span); return Task.CompletedTask; });
            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, received.Length));

            hostReceivedPrinter.SetResult((header.PrinterName, received.ToArray()));
        });

        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-TESTE", "127.0.0.1", hostPort, new[] { "EPSON L3250" }) },
        };
        var hostTable = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await hostTable.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        int localPort = GetFreeTcpPort();
        var mappingConfig = new ClientPrinterMappingConfig
        {
            Mappings = { new ClientPrinterMapping(localPort, "EPSON L3250") },
        };
        string spoolDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_spool_" + Guid.NewGuid());

        var loopbackServer = new LoopbackServer(mappingConfig, hostTable, NullLoggerFactory.Instance, spoolDir);
        await loopbackServer.StartAsync(CancellationToken.None);

        try
        {
            // Simula o driver Windows: abre conexão na porta loopback, escreve o job e fecha.
            using (var driverClient = new TcpClient())
            {
                await driverClient.ConnectAsync(IPAddress.Loopback, localPort);
                using NetworkStream driverStream = driverClient.GetStream();
                await driverStream.WriteAsync(jobBytes);
            }

            var (printerName, receivedData) = await hostReceivedPrinter.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await hostTask;

            Assert.Equal("EPSON L3250", printerName);
            Assert.Equal(jobBytes, receivedData);
        }
        finally
        {
            await loopbackServer.StopAsync(CancellationToken.None);
            if (Directory.Exists(spoolDir))
            {
                Directory.Delete(spoolDir, recursive: true);
            }
        }
    }
}
