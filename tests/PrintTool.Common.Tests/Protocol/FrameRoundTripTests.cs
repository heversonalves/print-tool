using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Common.Tests.Protocol;

public class FrameRoundTripTests
{
    [Fact]
    public async Task WriteThenRead_ControlMessage_RoundTrips()
    {
        using var stream = new MemoryStream();
        var header = new PrintJobRequestHeader("EPSON L3250", "relatorio.pdf", 12345);

        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobRequestHeader, header);
        stream.Position = 0;

        MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.PrintJobRequestHeader, envelope.Type);
        Assert.Equal(ProtocolVersion.Current, envelope.Version);

        var decoded = FrameReader.ReadMessage<PrintJobRequestHeader>(envelope);
        Assert.Equal(header, decoded);
    }

    [Fact]
    public async Task WriteThenRead_MultipleFramesInSequence_ReadsEachIndependently()
    {
        using var stream = new MemoryStream();
        var result1 = new PrintJobResult(true, 100);
        var result2 = new PrintJobResult(false, 0, "impressora offline");

        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, result1);
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, result2);
        stream.Position = 0;

        var first = FrameReader.ReadMessage<PrintJobResult>(await FrameReader.ReadFrameAsync(stream));
        var second = FrameReader.ReadMessage<PrintJobResult>(await FrameReader.ReadFrameAsync(stream));

        Assert.Equal(result1, first);
        Assert.Equal(result2, second);
    }

    [Fact]
    public async Task ReadFrameAsync_TruncatedStream_ThrowsProtocolException()
    {
        using var stream = new MemoryStream();
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, 1));

        byte[] full = stream.ToArray();
        using var truncated = new MemoryStream(full[..^1]);

        await Assert.ThrowsAsync<ProtocolException>(() => FrameReader.ReadFrameAsync(truncated));
    }

    [Fact]
    public async Task ReadFrameAsync_UnknownMessageType_ThrowsProtocolException()
    {
        using var stream = new MemoryStream();
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobResult, new PrintJobResult(true, 1));

        byte[] bytes = stream.ToArray();
        bytes[5] = 255; // byte de "tipo" no cabeçalho, valor inexistente no enum
        using var corrupted = new MemoryStream(bytes);

        await Assert.ThrowsAsync<ProtocolException>(() => FrameReader.ReadFrameAsync(corrupted));
    }

    [Fact]
    public async Task RawBytes_AfterHeaderFrame_StreamsInChunksWithoutLoss()
    {
        using var stream = new MemoryStream();
        byte[] jobData = new byte[200_000];
        new Random(42).NextBytes(jobData);

        var header = new PrintJobRequestHeader("EPSON L3250", "foto.jpg", jobData.Length);
        await FrameWriter.WriteMessageAsync(stream, MessageType.PrintJobRequestHeader, header);
        using (var source = new MemoryStream(jobData))
        {
            await FrameWriter.WriteRawAsync(stream, source, jobData.Length, bufferSize: 4096);
        }

        stream.Position = 0;
        var readHeader = FrameReader.ReadMessage<PrintJobRequestHeader>(await FrameReader.ReadFrameAsync(stream));
        Assert.Equal(header, readHeader);

        using var received = new MemoryStream();
        await FrameReader.ReadRawAsync(
            stream,
            readHeader.DataLength,
            (chunk, ct) =>
            {
                received.Write(chunk.Span);
                return Task.CompletedTask;
            },
            bufferSize: 8192);

        Assert.Equal(jobData, received.ToArray());
    }
}
