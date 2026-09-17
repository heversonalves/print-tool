using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PrintTool.Client.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Client.Forwarding;

/// <summary>
/// Encaminha um job de impressão para o Host que atualmente serve <paramref name="printerName"/>,
/// mantendo uma conexão TCP persistente e reconectando automaticamente em caso de falha.
/// Cada instância cuida de uma impressora remota; várias impressoras usam várias instâncias.
/// </summary>
public sealed class JobForwarder : IAsyncDisposable
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(2);
    private const int MaxAttemptsPerSend = 3;

    private readonly string _printerName;
    private readonly DiscoveredHostTable _hostTable;
    private readonly ILogger<JobForwarder> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public JobForwarder(string printerName, DiscoveredHostTable hostTable, ILogger<JobForwarder> logger)
    {
        _printerName = printerName;
        _hostTable = hostTable;
        _logger = logger;
    }

    /// <summary>
    /// Envia um job já completo (dados em <paramref name="jobData"/>, exatamente <paramref name="dataLength"/> bytes)
    /// para o Host, com algumas tentativas de reconexão em caso de falha transitória de rede.
    /// <paramref name="jobData"/> precisa ser seekable: é reposicionado no início antes de cada
    /// tentativa, já que uma tentativa anterior pode ter consumido parte dos bytes antes de falhar.
    /// </summary>
    public async Task<PrintJobResult> SendJobAsync(string jobName, Stream jobData, long dataLength, CancellationToken cancellationToken)
    {
        if (!jobData.CanSeek)
        {
            throw new ArgumentException("jobData precisa ser seekable, para permitir reenvio em caso de reconexão.", nameof(jobData));
        }

        Exception? lastError = null;

        for (int attempt = 0; attempt < MaxAttemptsPerSend; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }

            jobData.Position = 0;

            try
            {
                NetworkStream stream = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                var header = new PrintJobRequestHeader(_printerName, jobName, dataLength);
                await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobRequestHeader, header, cancellationToken).ConfigureAwait(false);
                await FrameWriter.WriteRawAsync(stream, jobData, dataLength, cancellationToken).ConfigureAwait(false);

                MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                return FrameReader.ReadMessage<PrintJobResult>(envelope);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ProtocolException or InvalidOperationException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Falha ao enviar job '{JobName}' para '{PrinterName}' (tentativa {Attempt}/{Max}).", jobName, _printerName, attempt + 1, MaxAttemptsPerSend);
                await DisconnectAsync().ConfigureAwait(false);
            }
        }

        throw new IOException($"Não foi possível entregar o job '{jobName}' para '{_printerName}' após {MaxAttemptsPerSend} tentativas.", lastError);
    }

    private static TimeSpan BackoffFor(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

    private async Task<NetworkStream> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tcpClient is { Connected: true } && _stream is not null)
            {
                return _stream;
            }

            DisconnectNoLock();

            if (!_hostTable.TryResolve(_printerName, out ResolvedHost? host) || host is null)
            {
                await _hostTable.RefreshAsync(DiscoveryTimeout, cancellationToken).ConfigureAwait(false);
                _hostTable.TryResolve(_printerName, out host);
            }

            if (host is null)
            {
                throw new InvalidOperationException($"Nenhum Host anunciando a impressora '{_printerName}' foi encontrado na rede.");
            }

            var client = new TcpClient();
            await client.ConnectAsync(host.Address, host.TcpPort, cancellationToken).ConfigureAwait(false);

            _tcpClient = client;
            _stream = client.GetStream();
            _logger.LogInformation("Conectado ao Host '{HostName}' ({Address}:{Port}) para a impressora '{PrinterName}'.", host.HostName, host.Address, host.TcpPort, _printerName);
            return _stream;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            DisconnectNoLock();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void DisconnectNoLock()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _stream = null;
        _tcpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
    }
}
