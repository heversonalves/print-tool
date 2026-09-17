using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;
using Xunit;

namespace PrintTool.Host.Tests.Network;

public class PrintServerTests : IAsyncLifetime
{
    private readonly FakePrinterManager _printerManager = new();
    private readonly SharedPrintersConfig _sharedPrinters = new() { SharedPrinterNames = { "EPSON L3250" } };
    private PrintServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = new PrintServer(
            _printerManager,
            _sharedPrinters,
            Options.Create(new PrintServerOptions { Port = 0 }),
            NullLogger<PrintServer>.Instance);

        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync(CancellationToken.None);
    }

    private async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.BoundPort!.Value);
        return client;
    }

    private static async Task<PrintJobResult> SendJobAsync(NetworkStream stream, string printerName, string jobName, byte[] data)
    {
        var header = new PrintJobRequestHeader(printerName, jobName, data.Length);
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobRequestHeader, header);
        using var source = new MemoryStream(data);
        await FrameWriter.WriteRawAsync(stream, source, data.Length);

        MessageEnvelope response = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.PrintJobResult, response.Type);
        return FrameReader.ReadMessage<PrintJobResult>(response);
    }

    [Fact]
    public async Task SubmitJob_ForSharedPrinter_SucceedsAndReachesPrinterManager()
    {
        using TcpClient client = await ConnectAsync();
        NetworkStream stream = client.GetStream();
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
    public async Task SubmitJob_ForPrinterNotShared_FailsWithoutTouchingPrinterManager()
    {
        using TcpClient client = await ConnectAsync();
        NetworkStream stream = client.GetStream();
        byte[] jobData = { 1, 2, 3, 4 };

        PrintJobResult result = await SendJobAsync(stream, "Brother HL-1212W", "teste.txt", jobData);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(_printerManager.Jobs);
    }

    [Fact]
    public async Task SubmitTwoJobs_OnSameConnection_BothProcessedInOrder()
    {
        using TcpClient client = await ConnectAsync();
        NetworkStream stream = client.GetStream();

        PrintJobResult rejected = await SendJobAsync(stream, "Impressora Desconhecida", "a.txt", new byte[] { 9 });
        PrintJobResult accepted = await SendJobAsync(stream, "EPSON L3250", "b.txt", new byte[] { 1, 2, 3 });

        Assert.False(rejected.Success);
        Assert.True(accepted.Success);
        var job = Assert.Single(_printerManager.Jobs);
        Assert.Equal("b.txt", job.JobName);
    }
}
