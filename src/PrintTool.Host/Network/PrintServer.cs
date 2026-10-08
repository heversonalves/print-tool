using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Common.Security;
using PrintTool.Host.Printers;
using PrintTool.Host.Security;

namespace PrintTool.Host.Network;

/// <summary>
/// Listener TCP com o protocolo próprio do Print Tool. Cada conexão é envolvida em TLS
/// (certificado autoassinado do <see cref="HostIdentity"/>) e precisa se autenticar — por
/// <see cref="MessageType.PairingRequest"/> (primeira vez, com código TOTP) ou
/// <see cref="MessageType.AuthenticateRequest"/> (token já emitido) — antes de qualquer
/// <see cref="MessageType.PrintJobRequestHeader"/> ser aceito. Uma vez autenticada, a conexão
/// pode enviar vários jobs em sequência (conexão persistente do lado do Client).
/// </summary>
public sealed class PrintServer : IHostedService
{
    private readonly IPrinterManager _printerManager;
    private readonly SharedPrintersConfig _sharedPrinters;
    private readonly HostIdentity _hostIdentity;
    private readonly TotpSecretStore _totpSecretStore;
    private readonly ClientTokenStore _clientTokenStore;
    private readonly ILogger<PrintServer> _logger;
    private readonly int _port;

    private TcpListener? _listener;
    private CancellationTokenSource? _stoppingCts;
    private Task? _acceptLoopTask;

    /// <summary>
    /// Porta TCP em que o listener efetivamente está escutando (útil em testes, que pedem porta 0 / dinâmica).
    /// </summary>
    public int? BoundPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port;

    public PrintServer(
        IPrinterManager printerManager,
        SharedPrintersConfig sharedPrinters,
        HostIdentity hostIdentity,
        TotpSecretStore totpSecretStore,
        ClientTokenStore clientTokenStore,
        IOptions<PrintServerOptions> options,
        ILogger<PrintServer> logger)
    {
        _printerManager = printerManager;
        _sharedPrinters = sharedPrinters;
        _hostIdentity = hostIdentity;
        _totpSecretStore = totpSecretStore;
        _clientTokenStore = clientTokenStore;
        _logger = logger;
        _port = options.Value.Port;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.LogInformation("PrintServer escutando na porta TCP {Port}.", _port);

        _acceptLoopTask = AcceptLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts?.Cancel();
        _listener?.Stop();

        if (_acceptLoopTask is not null)
        {
            await Task.WhenAny(_acceptLoopTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
        _logger.LogInformation("Client conectado: {Remote}.", remote);
        (Guid ClientId, string Token)? authentication = null;

        try
        {
            using (client)
            {
                await using NetworkStream networkStream = client.GetStream();
                await using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _hostIdentity.Certificate,
                        ClientCertificateRequired = false,
                    },
                    cancellationToken).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    MessageEnvelope envelope;
                    try
                    {
                        envelope = await FrameReader.ReadFrameAsync(sslStream, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ProtocolException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    switch (envelope.Type)
                    {
                        case MessageType.PairingRequest:
                            await HandlePairingAsync(sslStream, envelope, remote, cancellationToken).ConfigureAwait(false);
                            break;

                        case MessageType.AuthenticateRequest:
                            authentication = await HandleAuthenticateAsync(sslStream, envelope, remote, cancellationToken).ConfigureAwait(false);
                            break;

                        case MessageType.PrintJobRequestHeader:
                            // Revalida a cada job, não só uma vez por conexão: a conexão do Client é
                            // persistente (vários jobs na mesma conexão), então confiar só na checagem
                            // feita no AuthenticateRequest deixaria uma revogação sem efeito até a
                            // conexão cair por outro motivo.
                            bool authenticated = authentication is not null
                                && _clientTokenStore.Validate(authentication.Value.ClientId, authentication.Value.Token);
                            await HandlePrintJobAsync(sslStream, envelope, authenticated, remote, cancellationToken).ConfigureAwait(false);
                            break;

                        default:
                            _logger.LogWarning("Frame inesperado de {Remote}: {Type}.", remote, envelope.Type);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conexão com {Remote} encerrada por erro.", remote);
        }
        finally
        {
            _logger.LogInformation("Client desconectado: {Remote}.", remote);
        }
    }

    private async Task HandlePairingAsync(Stream stream, MessageEnvelope envelope, string remote, CancellationToken cancellationToken)
    {
        PairingRequest request = FrameReader.ReadMessage<PairingRequest>(envelope);

        if (!Totp.ValidateCode(_totpSecretStore.Secret, request.Code, DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Pareamento rejeitado para '{DisplayName}' ({Remote}): código TOTP inválido.", request.ClientDisplayName, remote);
            await FrameWriter.WriteMessageAsync(
                stream, MessageType.PairingResult,
                new PairingResult(Approved: false, Reason: "Código TOTP inválido."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string token = _clientTokenStore.IssueToken(request.ClientId, request.ClientDisplayName);
        _logger.LogInformation("Pareamento aprovado para '{DisplayName}' ({Remote}), ClientId {ClientId}.", request.ClientDisplayName, remote, request.ClientId);
        await FrameWriter.WriteMessageAsync(
            stream, MessageType.PairingResult,
            new PairingResult(Approved: true, HostId: _hostIdentity.HostId, Token: token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid ClientId, string Token)?> HandleAuthenticateAsync(Stream stream, MessageEnvelope envelope, string remote, CancellationToken cancellationToken)
    {
        AuthenticateRequest request = FrameReader.ReadMessage<AuthenticateRequest>(envelope);
        bool valid = _clientTokenStore.Validate(request.ClientId, request.Token);

        if (!valid)
        {
            _logger.LogWarning("Autenticação rejeitada para {Remote} (ClientId {ClientId}): token inválido ou revogado.", remote, request.ClientId);
        }

        await FrameWriter.WriteMessageAsync(
            stream, MessageType.AuthenticateResult,
            new AuthenticateResult(valid, valid ? null : "Token inválido ou revogado."),
            cancellationToken).ConfigureAwait(false);

        return valid ? (request.ClientId, request.Token) : null;
    }

    private async Task HandlePrintJobAsync(Stream stream, MessageEnvelope envelope, bool authenticated, string remote, CancellationToken cancellationToken)
    {
        PrintJobRequestHeader header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);

        if (!authenticated)
        {
            // Ainda é preciso consumir os bytes do job da conexão, senão o próximo frame lido fica corrompido.
            await FrameReader.ReadRawAsync(stream, header.DataLength, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Job de {Remote} rejeitado: conexão não autenticada.", remote);
            await FrameWriter.WriteMessageAsync(
                stream, MessageType.PrintJobResult,
                new PrintJobResult(false, 0, "Não autenticado. Pareie esta máquina antes de imprimir (veja: PrintTool.Client.exe pair)."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        PrintJobResult result = await ProcessJobAsync(header, stream, cancellationToken).ConfigureAwait(false);
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PrintJobResult> ProcessJobAsync(PrintJobRequestHeader header, Stream stream, CancellationToken cancellationToken)
    {
        if (!_sharedPrinters.IsShared(header.PrinterName))
        {
            // Ainda é preciso consumir os bytes do job da conexão, senão o próximo frame lido fica corrompido.
            await FrameReader.ReadRawAsync(stream, header.DataLength, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);
            return new PrintJobResult(false, 0, $"Impressora '{header.PrinterName}' não está compartilhada neste Host.");
        }

        IPrintJobWriter writer = await _printerManager.BeginJobAsync(header.PrinterName, header.JobName, cancellationToken).ConfigureAwait(false);
        try
        {
            await FrameReader.ReadRawAsync(
                stream,
                header.DataLength,
                (chunk, ct) => writer.WriteAsync(chunk, ct),
                cancellationToken).ConfigureAwait(false);

            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return new PrintJobResult(true, writer.BytesWritten);
        }
        catch (Exception ex)
        {
            await writer.AbortAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(ex, "Falha ao processar job '{JobName}' para '{PrinterName}'.", header.JobName, header.PrinterName);
            return new PrintJobResult(false, writer.BytesWritten, ex.Message);
        }
    }
}
