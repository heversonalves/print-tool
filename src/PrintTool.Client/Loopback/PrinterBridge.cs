using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PrintTool.Client.Forwarding;

namespace PrintTool.Client.Loopback;

/// <summary>
/// Um listener TCP em 127.0.0.1:<see cref="_localPort"/> para uma impressora específica.
/// O driver Windows (via "Porta TCP/IP Padrão") abre uma conexão por job, escreve os dados
/// brutos e fecha a conexão — o fechamento é o próprio marcador de fim do job.
/// </summary>
internal sealed class PrinterBridge : IAsyncDisposable
{
    // Mantém a conexão com o Host viva mesmo sem jobs pendentes, para que o monitor de
    // conectividade do Host sempre tenha uma conexão autenticada pra observar.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMinutes(1);

    private readonly int _localPort;
    private readonly string _printerName;
    private readonly JobForwarder _forwarder;
    private readonly LocalJobQueue _queue;
    private readonly string _spoolDirectory;
    private readonly ILogger _logger;

    private TcpListener? _listener;
    private CancellationTokenSource? _stoppingCts;
    private Task? _acceptLoopTask;
    private Task? _keepAliveTask;

    public PrinterBridge(int localPort, string printerName, JobForwarder forwarder, LocalJobQueue queue, string spoolDirectory, ILogger logger)
    {
        _localPort = localPort;
        _printerName = printerName;
        _forwarder = forwarder;
        _queue = queue;
        _spoolDirectory = spoolDirectory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_spoolDirectory);

        _stoppingCts = new CancellationTokenSource();
        _queue.Start(_stoppingCts.Token);

        _listener = new TcpListener(IPAddress.Loopback, _localPort);
        _listener.Start();
        _logger.LogInformation("Loopback para '{PrinterName}' escutando em 127.0.0.1:{Port}.", _printerName, _localPort);

        _acceptLoopTask = AcceptLoopAsync(_stoppingCts.Token);
        _keepAliveTask = KeepAliveLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts?.Cancel();
        _listener?.Stop();

        var pending = new List<Task>();
        if (_acceptLoopTask is not null)
        {
            pending.Add(_acceptLoopTask);
        }
        if (_keepAliveTask is not null)
        {
            pending.Add(_keepAliveTask);
        }

        if (pending.Count > 0)
        {
            await Task.WhenAny(Task.WhenAll(pending), Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Chama <see cref="JobForwarder.KeepAliveAsync"/> logo ao subir (pra já conectar sem
    /// esperar o primeiro job ou o primeiro intervalo) e depois a cada <see cref="KeepAliveInterval"/>.
    /// </summary>
    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _forwarder.KeepAliveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(KeepAliveInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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

            _ = HandleConnectionAsync(client, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(_spoolDirectory, $"{Guid.NewGuid():N}.spool");

        try
        {
            long length;
            using (client)
            await using (NetworkStream netStream = client.GetStream())
            {
                await using FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await netStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                length = fileStream.Length;
            }

            if (length == 0)
            {
                return;
            }

            string jobName = $"job-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            bool connectivityFailure = false;

            try
            {
                await using FileStream readStream = new(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var result = await _forwarder.SendJobAsync(jobName, readStream, length, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    // Rejeição lógica do Host (ex.: impressora não compartilhada) — reenviar não ajuda.
                    _logger.LogError("Host rejeitou o job '{JobName}' para '{PrinterName}': {Error}", jobName, _printerName, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                // Falha de conectividade — o JobForwarder já tentou reconectar e desistiu.
                connectivityFailure = true;
                _logger.LogWarning(ex, "Host indisponível para '{PrinterName}'; job '{JobName}' vai para a fila local.", _printerName, jobName);
            }

            if (connectivityFailure)
            {
                await _queue.EnqueueAsync(tempPath, jobName, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao processar job recebido em 127.0.0.1:{Port} para '{PrinterName}'.", _localPort, _printerName);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _queue.DisposeAsync().ConfigureAwait(false);
        await _forwarder.DisposeAsync().ConfigureAwait(false);
    }
}
