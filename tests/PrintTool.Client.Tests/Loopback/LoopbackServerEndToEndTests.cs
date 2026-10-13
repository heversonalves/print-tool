using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Client.Loopback;
using PrintTool.Client.Security;
using PrintTool.Client.Tests.TestSupport;
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
    public async Task DriverWritesJob_AuthenticatedAndFlowsThroughLoopbackAndForwarder_ReachesFakeHost()
    {
        var hostId = Guid.NewGuid();
        using X509Certificate2 hostCertificate = FakeTlsHost.CreateSelfSignedCertificate();
        string thumbprint = FakeTlsHost.ThumbprintOf(hostCertificate);

        // Fake Host: faz o handshake TLS, exige AuthenticateRequest e responde como o PrintServer real responderia.
        using var hostListener = new TcpListener(IPAddress.Loopback, 0);
        hostListener.Start();
        int hostPort = ((IPEndPoint)hostListener.LocalEndpoint).Port;

        byte[] jobBytes = new byte[30_000];
        new Random(7).NextBytes(jobBytes);

        var hostReceivedPrinter = new TaskCompletionSource<(string PrinterName, byte[] Data)>();
        var hostTask = Task.Run(async () =>
        {
            await using SslStream sslStream = await FakeTlsHost.AcceptAndAuthenticateAsync(hostListener, hostCertificate);
            await FakeTlsHost.ExpectAuthenticateRequestAsync(sslStream);

            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(sslStream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

            using var received = new MemoryStream();
            await FrameReader.ReadRawAsync(sslStream, header.DataLength, (chunk, _) => { received.Write(chunk.Span); return Task.CompletedTask; });
            await FrameWriter.WriteMessageAsync(sslStream, MessageType.PrintJobResult, new PrintJobResult(true, received.Length));

            hostReceivedPrinter.SetResult((header.PrinterName, received.ToArray()));
        });

        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-TESTE", "127.0.0.1", hostPort, new[] { "EPSON L3250" }, hostId, thumbprint) },
        };
        var hostTable = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await hostTable.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        int localPort = GetFreeTcpPort();
        string mappingConfigPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_printers_" + Guid.NewGuid() + ".json");
        new ClientPrinterMappingConfig
        {
            Mappings = { new ClientPrinterMapping(localPort, "EPSON L3250") },
        }.Save(mappingConfigPath);
        string spoolDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_spool_" + Guid.NewGuid());
        string tokensPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_tokens_" + Guid.NewGuid() + ".json");
        string identityPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_identity_" + Guid.NewGuid() + ".json");

        var clientIdentity = ClientIdentity.LoadOrCreate(identityPath);
        var tokenStore = new HostTokenStore(tokensPath);
        tokenStore.Save(new HostTokenEntry(hostId, "HOST-TESTE", thumbprint, "token-de-teste-ja-pareado", DateTimeOffset.UtcNow));

        var loopbackServer = new LoopbackServer(mappingConfigPath, hostTable, clientIdentity, tokenStore, NullLoggerFactory.Instance, spoolDir);
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
            foreach (string path in new[] { spoolDir })
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            foreach (string path in new[] { tokensPath, identityPath, mappingConfigPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public async Task MappingConfigChangedExternally_BridgesStartAndStopWithoutRestart()
    {
        var stub = new StubDiscoveryClient(); // sem Host nenhum: não importa pra este teste, só o ciclo de vida do bridge.
        var hostTable = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);

        string mappingConfigPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_printers_" + Guid.NewGuid() + ".json");
        new ClientPrinterMappingConfig().Save(mappingConfigPath); // começa sem nenhum mapeamento.
        string spoolDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_spool_" + Guid.NewGuid());
        string tokensPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_tokens_" + Guid.NewGuid() + ".json");
        string identityPath = Path.Combine(Path.GetTempPath(), "PrintToolTests_identity_" + Guid.NewGuid() + ".json");

        var clientIdentity = ClientIdentity.LoadOrCreate(identityPath);
        var tokenStore = new HostTokenStore(tokensPath);

        var loopbackServer = new LoopbackServer(mappingConfigPath, hostTable, clientIdentity, tokenStore, NullLoggerFactory.Instance, spoolDir);
        await loopbackServer.StartAsync(CancellationToken.None);

        try
        {
            int localPort = GetFreeTcpPort();
            Assert.False(await CanConnectAsync(localPort), "Não deveria haver nenhum bridge escutando antes do mapeamento existir.");

            // Simula o app de administração adicionando uma impressora nova, sem reiniciar o serviço.
            new ClientPrinterMappingConfig
            {
                Mappings = { new ClientPrinterMapping(localPort, "EPSON L3250") },
            }.Save(mappingConfigPath);

            Assert.True(await WaitUntilAsync(() => CanConnectAsync(localPort), TimeSpan.FromSeconds(5)),
                "O bridge da porta nova deveria subir sozinho após o arquivo mudar.");

            // Simula a remoção da impressora pelo app de administração.
            new ClientPrinterMappingConfig().Save(mappingConfigPath);

            Assert.True(await WaitUntilAsync(async () => !await CanConnectAsync(localPort), TimeSpan.FromSeconds(5)),
                "O bridge deveria parar de escutar depois que o mapeamento foi removido.");
        }
        finally
        {
            await loopbackServer.StopAsync(CancellationToken.None);
            foreach (string path in new[] { spoolDir })
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            foreach (string path in new[] { tokensPath, identityPath, mappingConfigPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(300));
            return true;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }
            await Task.Delay(100).ConfigureAwait(false);
        }
        return false;
    }
}
