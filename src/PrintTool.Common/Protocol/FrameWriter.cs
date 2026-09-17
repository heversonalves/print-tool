using System.Buffers.Binary;
using System.Text.Json;

namespace PrintTool.Common.Protocol;

/// <summary>
/// Escreve frames de controle em um stream de rede.
/// Formato do frame: [4 bytes big-endian: tamanho do payload][1 byte: versão][1 byte: tipo][payload].
/// </summary>
public static class FrameWriter
{
    private const int HeaderSize = 4 + 1 + 1;

    public static async Task WriteMessageAsync<T>(Stream stream, MessageType type, T message, CancellationToken cancellationToken = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await WriteFrameAsync(stream, type, payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteFrameAsync(Stream stream, MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), payload.Length);
        header[4] = ProtocolVersion.Current;
        header[5] = (byte)type;

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Copia exatamente <paramref name="length"/> bytes de <paramref name="source"/> para
    /// <paramref name="stream"/>, sem envelope de frame. Usado para os dados brutos do job de
    /// impressão, que trafegam imediatamente após o frame <see cref="MessageType.PrintJobRequestHeader"/>.
    /// </summary>
    public static async Task WriteRawAsync(Stream stream, Stream source, long length, CancellationToken cancellationToken = default, int bufferSize = 81920)
    {
        await FrameIO.CopyExactAsync(source, stream, length, cancellationToken, bufferSize).ConfigureAwait(false);
    }
}
