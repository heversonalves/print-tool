using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Common.Security;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;
using PrintTool.Host.Security;
using Xunit;

namespace PrintTool.Host.Tests.Network;

public class PrintServerTests : IAsyncLifetime, IDisposable
{
    private readonly FakePrinterManager _printerManager = new();
    private readonly SharedPrintersConfig _sharedPrinters = new() { SharedPrinterNames = { "EPSON L3250" } };
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_printserver_" + Guid.NewGuid());

    private HostIdentity _hostIdentity = null!;
    private TotpSecretStore _totpSecretStore = null!;
    private ClientTokenStore _clientTokenStore = null!;
    private PrintServer _server = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _hostIdentity = HostIdentity.LoadOrCreate(_tempDir);
        _totpSecretStore = TotpSecretStore.LoadOrCreate(Path.Combine(_tempDir, "totp-secret.json"));
        _clientTokenStore = ClientTokenStore.LoadOrCreate(Path.Combine(_tempDir, "tokens.json"));

        _server = new PrintServer(
            _printerManager,
            _sharedPrinters,
            _hostIdentity,
            _totpSecretStore,
            _clientTokenStore,
            Options.Create(new PrintServerOptions { Port = 0 }),
            NullLogger<PrintServer>.Instance);

        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync(CancellationToken.None);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.BoundPort!.Value);
        return client;
    }

    private static async Task<SslStream> AuthenticateTlsAsync(TcpClient client)
    {
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("printtool-host-teste");
        return ssl;
    }

    private static async Task<PrintJobResult> SendJobAsync(Stream stream, string printerName, string jobName, byte[] data)
    {
        var header = new PrintJobRequestHeader(printerName, jobName, data.Length);
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobRequestHeader, header);
        using var source = new MemoryStream(data);
        await FrameWriter.WriteRawAsync(stream, source, data.Length);

        MessageEnvelope response = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.PrintJobResult, response.Type);
        return FrameReader.ReadMessage<PrintJobResult>(response);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(Stream stream, Guid clientId, string token)
    {
        await FrameWriter.WriteMessageAsync(stream, MessageType.AuthenticateRequest, new AuthenticateRequest(clientId, token));
        MessageEnvelope response = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.AuthenticateResult, response.Type);
        return FrameReader.ReadMessage<AuthenticateResult>(response);
    }

    private static async Task<PairingResult> PairAsync(Stream stream, Guid clientId, string displayName, string code)
    {
        await FrameWriter.WriteMessageAsync(stream, MessageType.PairingRequest, new PairingRequest(clientId, displayName, code));
        MessageEnvelope response = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.PairingResult, response.Type);
        return FrameReader.ReadMessage<PairingResult>(response);
    }

    [Fact]
    public async Task SubmitJob_Authenticated_ForSharedPrinter_SucceedsAndReachesPrinterManager()
    {
        var clientId = Guid.NewGuid();
        string token = _clientTokenStore.IssueToken(clientId, "Notebook de Teste");

        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);

        AuthenticateResult authResult = await AuthenticateAsync(stream, clientId, token);
        Assert.True(authResult.Success);

        byte[] jobData = new byte[50_000];
        new Random(1).NextBytes(jobData);
        PrintJobResult result = await SendJobAsync(stream, "EPSON L3250", "nota-fiscal.pdf", jobData);

        Assert.True(result.Success);
        Assert.Equal(jobData.Length, result.BytesWritten);

        var job = Assert.Single(_printerManager.Jobs);
        Assert.Equal("EPSON L3250", job.PrinterName);
        Assert.Equal("nota-fiscal.pdf", job.JobName);
        Assert.Equal(jobData, job.Data);
        Assert.False(job.Aborted);
    }

    [Fact]
    public async Task SubmitJob_Authenticated_ForPrinterNotShared_FailsWithoutTouchingPrinterManager()
    {
        var clientId = Guid.NewGuid();
        string token = _clientTokenStore.IssueToken(clientId, "Notebook de Teste");

        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);
        await AuthenticateAsync(stream, clientId, token);

        PrintJobResult result = await SendJobAsync(stream, "Brother HL-1212W", "teste.txt", new byte[] { 1, 2, 3, 4 });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(_printerManager.Jobs);
    }

    [Fact]
    public async Task SubmitTwoJobs_OnSameAuthenticatedConnection_BothProcessedInOrder()
    {
        var clientId = Guid.NewGuid();
        string token = _clientTokenStore.IssueToken(clientId, "Notebook de Teste");

        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);
        await AuthenticateAsync(stream, clientId, token);

        PrintJobResult rejected = await SendJobAsync(stream, "Impressora Desconhecida", "a.txt", new byte[] { 9 });
        PrintJobResult accepted = await SendJobAsync(stream, "EPSON L3250", "b.txt", new byte[] { 1, 2, 3 });

        Assert.False(rejected.Success);
        Assert.True(accepted.Success);
        var job = Assert.Single(_printerManager.Jobs);
        Assert.Equal("b.txt", job.JobName);
    }

    [Fact]
    public async Task SubmitJob_WithoutAuthentication_IsRejectedAndPrinterManagerUntouched()
    {
        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);

        PrintJobResult result = await SendJobAsync(stream, "EPSON L3250", "nota-fiscal.pdf", new byte[] { 1, 2, 3 });

        Assert.False(result.Success);
        Assert.Empty(_printerManager.Jobs);
    }

    [Fact]
    public async Task AuthenticateRequest_WithUnknownToken_IsRejected()
    {
        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);

        AuthenticateResult result = await AuthenticateAsync(stream, Guid.NewGuid(), "token-inexistente");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AuthenticateRequest_WithRevokedToken_IsRejected()
    {
        var clientId = Guid.NewGuid();
        string token = _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        _clientTokenStore.Revoke(clientId);

        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);

        AuthenticateResult result = await AuthenticateAsync(stream, clientId, token);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PairingRequest_WithValidTotpCode_IssuesTokenThatAuthenticatesSuccessfully()
    {
        var clientId = Guid.NewGuid();
        string code = Totp.GenerateCode(_totpSecretStore.Secret, DateTimeOffset.UtcNow);

        using TcpClient pairingClient = await ConnectAsync();
        await using SslStream pairingStream = await AuthenticateTlsAsync(pairingClient);
        PairingResult pairingResult = await PairAsync(pairingStream, clientId, "Notebook Novo", code);

        Assert.True(pairingResult.Approved);
        Assert.Equal(_hostIdentity.HostId, pairingResult.HostId);
        Assert.False(string.IsNullOrEmpty(pairingResult.Token));

        using TcpClient jobClient = await ConnectAsync();
        await using SslStream jobStream = await AuthenticateTlsAsync(jobClient);
        AuthenticateResult authResult = await AuthenticateAsync(jobStream, clientId, pairingResult.Token!);

        Assert.True(authResult.Success);
    }

    [Fact]
    public async Task PairingRequest_WithInvalidTotpCode_IsRejectedAndIssuesNoToken()
    {
        using TcpClient client = await ConnectAsync();
        await using SslStream stream = await AuthenticateTlsAsync(client);

        PairingResult result = await PairAsync(stream, Guid.NewGuid(), "Notebook Novo", "000000");

        Assert.False(result.Approved);
        Assert.Null(result.Token);
    }
}
