using Microsoft.Extensions.Logging;
using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Client.Forwarding;

/// <summary>
/// Fila em disco para jobs que não puderam ser entregues ao Host por falha de conectividade.
/// Cada job fica como um par de arquivos (<c>.data</c> com os bytes, <c>.meta</c> com o nome do job)
/// até ser entregue com sucesso; um laço de fundo tenta reenviar periodicamente, e também
/// imediatamente após um novo job entrar na fila.
/// </summary>
public sealed class LocalJobQueue : IAsyncDisposable
{
    private readonly string _printerName;
    private readonly JobForwarder _forwarder;
    private readonly string _queueDirectory;
    private readonly ILogger _logger;
    private readonly TimeSpan _drainInterval;
    private readonly SemaphoreSlim _drainSignal = new(0);

    private CancellationTokenSource? _stoppingCts;
    private Task? _drainLoopTask;

    public LocalJobQueue(string printerName, JobForwarder forwarder, string queueDirectory, ILogger logger, TimeSpan? drainInterval = null)
    {
        _printerName = printerName;
        _forwarder = forwarder;
        _queueDirectory = queueDirectory;
        _logger = logger;
        _drainInterval = drainInterval ?? TimeSpan.FromSeconds(10);
    }

    public void Start(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_queueDirectory);
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _drainLoopTask = DrainLoopAsync(_stoppingCts.Token);
    }

    public async Task StopAsync()
    {
        _stoppingCts?.Cancel();
        if (_drainLoopTask is not null)
        {
            try
            {
                await _drainLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // esperado ao parar
            }
        }
    }

    /// <summary>
    /// Move o arquivo de spool já gravado em disco para a fila persistente (o chamador perde
    /// a posse de <paramref name="sourceFilePath"/> a partir daqui) e acorda o dreno imediatamente.
    /// </summary>
    public async Task EnqueueAsync(string sourceFilePath, string jobName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_queueDirectory);

        string id = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        string dataPath = Path.Combine(_queueDirectory, id + ".data");
        string metaPath = Path.Combine(_queueDirectory, id + ".meta");

        File.Move(sourceFilePath, dataPath);
        await File.WriteAllTextAsync(metaPath, jobName, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Job '{JobName}' para '{PrinterName}' colocado na fila local (Host indisponível).", jobName, _printerName);

        if (_drainSignal.CurrentCount == 0)
        {
            _drainSignal.Release();
        }
    }

    private async Task DrainLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await TryDrainOnceAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await Task.WhenAny(
                    Task.Delay(_drainInterval, cancellationToken),
                    _drainSignal.WaitAsync(cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TryDrainOnceAsync(CancellationToken cancellationToken)
    {
        foreach (string metaPath in PendingJobsInOrder())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            string dataPath = Path.ChangeExtension(metaPath, ".data");
            if (!File.Exists(dataPath))
            {
                File.Delete(metaPath);
                continue;
            }

            string jobName = await File.ReadAllTextAsync(metaPath, cancellationToken).ConfigureAwait(false);

            try
            {
                PrintJobResult result;
                await using (FileStream stream = new(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    result = await _forwarder.SendJobAsync(jobName, stream, stream.Length, cancellationToken).ConfigureAwait(false);
                }

                if (!result.Success)
                {
                    _logger.LogError("Host rejeitou job em fila '{JobName}' para '{PrinterName}': {Error}", jobName, _printerName, result.ErrorMessage);
                }
                else
                {
                    _logger.LogInformation("Job em fila '{JobName}' para '{PrinterName}' entregue com sucesso.", jobName, _printerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Host ainda indisponível para '{PrinterName}'; job '{JobName}' permanece na fila.", _printerName, jobName);
                return;
            }

            File.Delete(dataPath);
            File.Delete(metaPath);
        }
    }

    private IEnumerable<string> PendingJobsInOrder()
        => Directory.Exists(_queueDirectory)
            ? Directory.EnumerateFiles(_queueDirectory, "*.meta").OrderBy(p => p, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stoppingCts?.Dispose();
        _drainSignal.Dispose();
    }
}
