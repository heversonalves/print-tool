using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Host.Printers;

namespace PrintTool.Host.Network;

/// <summary>
/// Listener TCP com o protocolo próprio do Print Tool. Cada conexão de Client fica aberta
/// e pode enviar vários jobs em sequência (conexão persistente do lado do Client).
/// </summary>
public sealed class PrintServer : IHostedService
{
    private readonly IPrinterManager _printerManager;
    private readonly SharedPrintersConfig _sharedPrinters;
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
        IOptions<PrintServerOptions> options,
        ILogger<PrintServer> logger)
    {
        _printerManager = printerManager;
        _sharedPrinters = sharedPrinters;
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

        try
        {
            using (client)
            {
                await using NetworkStream stream = client.GetStream();

                while (!cancellationToken.IsCancellationRequested)
                {
                    MessageEnvelope envelope;
                    try
                    {
                        envelope = await FrameReader.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ProtocolException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    if (envelope.Type != MessageType.PrintJobRequestHeader)
                    {
                        _logger.LogWarning("Frame inesperado de {Remote}: {Type}.", remote, envelope.Type);
                        continue;
                    }

                    PrintJobRequestHeader header = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);
                    PrintJobResult result = await ProcessJobAsync(header, stream, cancellationToken).ConfigureAwait(false);

                    await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, result, cancellationToken).ConfigureAwait(false);
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

    private async Task<PrintJobResult> ProcessJobAsync(PrintJobRequestHeader header, NetworkStream stream, CancellationToken cancellationToken)
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
