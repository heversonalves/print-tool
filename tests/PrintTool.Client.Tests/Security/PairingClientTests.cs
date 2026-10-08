using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using PrintTool.Client.Discovery;
using PrintTool.Client.Security;
using PrintTool.Client.Tests.TestSupport;
using PrintTool.Common.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.Security;

public class PairingClientTests : IDisposable
{
    private sealed class StubDiscoveryClient : IDiscoveryClient
    {
        public IReadOnlyList<DiscoveryAnnouncement> NextResult { get; set; } = Array.Empty<DiscoveryAnnouncement>();

        public Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(NextResult);
    }

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_pairingclient_" + Guid.NewGuid());

    public PairingClientTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ClientIdentity BuildClientIdentity() => ClientIdentity.LoadOrCreate(Path.Combine(_tempDir, "client-identity.json"));

    private HostTokenStore BuildTokenStore() => new(Path.Combine(_tempDir, "tokens.json"));

    private static async Task<DiscoveredHostTable> BuildResolvedTableAsync(Guid hostId, int port, string printerName, string thumbprint)
    {
        var stub = new StubDiscoveryClient
        {
            NextResult = new[] { new DiscoveryAnnouncement(Guid.NewGuid(), "HOST-TESTE", "127.0.0.1", port, new[] { printerName }, hostId, thumbprint) },
        };
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        await table.RefreshAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        return table;
    }

    [Fact]
    public async Task PairAsync_HostApproves_SavesTokenAndPinnedThumbprint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        string thumbprint = FakeTlsHost.ThumbprintOf(certificate);
        var hostId = Guid.NewGuid();
        const string issuedToken = "token-emitido-pelo-host";

        var serverTask = Task.Run(async () =>
        {
            await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, certificate);

            MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
            var request = FrameReader.ReadMessage<PairingRequest>(envelope);
            await FrameWriter.WriteMessageAsync(
                stream, MessageType.PairingResult,
                new PairingResult(Approved: true, HostId: hostId, Token: issuedToken));
            return request;
        });

        DiscoveredHostTable table = await BuildResolvedTableAsync(hostId, port, "EPSON L3250", thumbprint);
        ClientIdentity clientIdentity = BuildClientIdentity();
        HostTokenStore tokenStore = BuildTokenStore();
        var pairingClient = new PairingClient(table, clientIdentity, tokenStore);

        PairingOutcome outcome = await pairingClient.PairAsync("EPSON L3250", "123456", CancellationToken.None);
        PairingRequest receivedRequest = await serverTask;

        Assert.True(outcome.Success);
        Assert.Equal(clientIdentity.ClientId, receivedRequest.ClientId);
        Assert.Equal("123456", receivedRequest.Code);

        HostTokenEntry? saved = tokenStore.TryGet(hostId);
        Assert.NotNull(saved);
        Assert.Equal(issuedToken, saved!.Token);
        Assert.Equal(thumbprint, saved.CertThumbprint);
    }

    [Fact]
    public async Task PairAsync_HostRejectsCode_DoesNotSaveToken()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using X509Certificate2 certificate = FakeTlsHost.CreateSelfSignedCertificate();
        string thumbprint = FakeTlsHost.ThumbprintOf(certificate);
        var hostId = Guid.NewGuid();

        var serverTask = Task.Run(async () =>
        {
            await using SslStream stream = await FakeTlsHost.AcceptAndAuthenticateAsync(listener, certificate);
            await FrameReader.ReadFrameAsync(stream);
            await FrameWriter.WriteMessageAsync(
                stream, MessageType.PairingResult,
                new PairingResult(Approved: false, Reason: "Código TOTP inválido."));
        });

        DiscoveredHostTable table = await BuildResolvedTableAsync(hostId, port, "EPSON L3250", thumbprint);
        HostTokenStore tokenStore = BuildTokenStore();
        var pairingClient = new PairingClient(table, BuildClientIdentity(), tokenStore);

        PairingOutcome outcome = await pairingClient.PairAsync("EPSON L3250", "000000", CancellationToken.None);
        await serverTask;

        Assert.False(outcome.Success);
        Assert.Equal("Código TOTP inválido.", outcome.ErrorMessage);
        Assert.Null(tokenStore.TryGet(hostId));
    }

    [Fact]
    public async Task PairAsync_PrinterNeverAnnounced_FailsWithoutConnecting()
    {
        var stub = new StubDiscoveryClient(); // nunca resolve nada
        var table = new DiscoveredHostTable(stub, NullLogger<DiscoveredHostTable>.Instance);
        var pairingClient = new PairingClient(table, BuildClientIdentity(), BuildTokenStore());

        PairingOutcome outcome = await pairingClient.PairAsync("Impressora Fantasma", "123456", CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains("Nenhum Host", outcome.ErrorMessage);
    }
}
