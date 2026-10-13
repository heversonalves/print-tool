using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Client.Forwarding;
using PrintTool.Client.Security;
using PrintTool.Client.Tests.TestSupport;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Forwarding;

public class JobForwarderTests : IDisposable
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
    }

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_jobforwarder_" + Guid.NewGuid());

    public JobForwarderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ClientIdentity BuildClientIdentity() => ClientIdentity.LoadOrCreate(Path.Combine(_tempDir, "client-identity.json"));

    private HostTokenStore BuildTokenStore() => new(Path.Combine(_tempDir, "tokens.json"));

    private static async Task<(DiscoveredHostTable Table, Guid HostId, string Thumbprint)> BuildResolvedTableAsync(
        int port, string printerName, X509Certificate2 certificate)
    {
        var hostId = Guid.NewGuid();
        string thumbprint = FakeTlsHost.ThumbprintOf(certificate);
        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-TESTE", "127.0.0.1", port, new[] { printerName }, hostId, thumbprint) },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        return (table, hostId, thumbprint);
    }

    [Fact]
    public async Task SendJobAsync_HostAvailableAndPaired_DeliversJobAndReturnsResult()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        byte[] jobData = { 1, 2, 3, 4, 5 };
        var serverTask = Task.Run(async () =>
        {
            await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, certificate);
            await FakeTlsHost.ExpectAuthenticateRequestAsync(stream);

            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

            using var received = new MemoryStream();
            await FrameReader.ReadRawAsync(stream, header.DataLength, (chunk, _) => { received.Write(chunk.Span); return Task.CompletedTask; });

            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, received.Length));
            return received.ToArray();
        });

        (DiscoveredHostTable table, Guid hostId, string thumbprint) = await BuildResolvedTableAsync(port, "EPSON L3250", certificate);
        HostTokenStore tokenStore = BuildTokenStore();
        tokenStore.Save(new HostTokenEntry(hostId, "HOST-TESTE", thumbprint, "token-valido", DateTimeOffset.UtcNow));

        var forwarder = new JobForwarder("EPSON L3250", table, BuildClientIdentity(), tokenStore, NullLogger<JobForwarder>.Instance);

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

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        var serverTask = Task.Run(async () =>
        {
            using (TcpClient first = await listener.AcceptTcpClientAsync())
            {
                // Simula o Host caindo no meio da conexão, sem responder nada.
            }

            await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, certificate);
            await FakeTlsHost.ExpectAuthenticateRequestAsync(stream);

            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);
            await FrameReader.ReadRawAsync(stream, header.DataLength, (_, _) => Task.CompletedTask);
            await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, header.DataLength));
        });

        (DiscoveredHostTable table, Guid hostId, string thumbprint) = await BuildResolvedTableAsync(port, "EPSON L3250", certificate);
        HostTokenStore tokenStore = BuildTokenStore();
        tokenStore.Save(new HostTokenEntry(hostId, "HOST-TESTE", thumbprint, "token-valido", DateTimeOffset.UtcNow));

        var forwarder = new JobForwarder("EPSON L3250", table, BuildClientIdentity(), tokenStore, NullLogger<JobForwarder>.Instance);

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
        var forwarder = new JobForwarder("Impressora Fantasma", table, BuildClientIdentity(), BuildTokenStore(), NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<IOException>(() => forwarder.SendJobAsync("x.txt", source, 1, CancellationToken.None));

        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task SendJobAsync_HostAvailableButNotPaired_ThrowsAfterRetriesWithoutConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        (DiscoveredHostTable table, _, _) = await BuildResolvedTableAsync(port, "EPSON L3250", certificate);

        // Sem token salvo no HostTokenStore: esta máquina nunca foi pareada com este Host.
        var forwarder = new JobForwarder("EPSON L3250", table, BuildClientIdentity(), BuildTokenStore(), NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        IOException exception = await Assert.ThrowsAsync<IOException>(() => forwarder.SendJobAsync("x.txt", source, 3, CancellationToken.None));

        Assert.Contains("pareada", exception.InnerException?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(listener.Pending()); // o Host nunca chegou a ser contatado

        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task KeepAliveAsync_HostAvailableAndPaired_AuthenticatesWithoutAnyJob()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        var serverTask = Task.Run(async () =>
        {
            await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, certificate);
            await FakeTlsHost.ExpectAuthenticateRequestAsync(stream);
        });

        (DiscoveredHostTable table, Guid hostId, string thumbprint) = await BuildResolvedTableAsync(port, "EPSON L3250", certificate);
        HostTokenStore tokenStore = BuildTokenStore();
        tokenStore.Save(new HostTokenEntry(hostId, "HOST-TESTE", thumbprint, "token-valido", DateTimeOffset.UtcNow));

        var forwarder = new JobForwarder("EPSON L3250", table, BuildClientIdentity(), tokenStore, NullLogger<JobForwarder>.Instance);

        // Sem nenhum job: é exatamente isso que faz o Host ver a máquina como conectada
        // mesmo ociosa, pro monitor de conectividade observar um estado real.
        await forwarder.KeepAliveAsync(CancellationToken.None);

        await serverTask;
        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task KeepAliveAsync_HostUnavailable_DoesNotThrow()
    {
        var stub = new StubDiscoveryClient(); // nunca resolve nada
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        var forwarder = new JobForwarder("Impressora Fantasma", table, BuildClientIdentity(), BuildTokenStore(), NullLogger<JobForwarder>.Instance);

        // Diferente de SendJobAsync, não há job pra propagar o erro: a falha só é logada.
        await forwarder.KeepAliveAsync(CancellationToken.None);

        await forwarder.DisposeAsync();
    }

    [Fact]
    public async Task SendJobAsync_HostPresentsUnexpectedCertificate_ThrowsAfterRetries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 realCertificate = FakeTlsHost.CreateSelfSignedCertificate();
        using X509Certificate2 impostorCertificate = FakeTlsHost.CreateSelfSignedCertificate();

        var serverTask = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, impostorCertificate);
                }
                catch
                {
                    // esperado: o Client recusa o handshake por thumbprint divergente.
                }
            }
        });

        (DiscoveredHostTable table, Guid hostId, _) = await BuildResolvedTableAsync(port, "EPSON L3250", realCertificate);
        HostTokenStore tokenStore = BuildTokenStore();
        // Fixa (TOFU) o thumbprint do certificado "real" — o impostor apresenta outro certificado.
        tokenStore.Save(new HostTokenEntry(hostId, "HOST-TESTE", FakeTlsHost.ThumbprintOf(realCertificate), "token-valido", DateTimeOffset.UtcNow));

        var forwarder = new JobForwarder("EPSON L3250", table, BuildClientIdentity(), tokenStore, NullLogger<JobForwarder>.Instance);

        using var source = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<IOException>(() => forwarder.SendJobAsync("x.txt", source, 1, CancellationToken.None));

        await forwarder.DisposeAsync();
        await serverTask;
    }
}
