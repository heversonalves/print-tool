using System.Buffers.Binary;
using System.Text.Json;

namespace PrintTool.Common.Protocol;

/// <summary>
/// Lê frames de controle de um stream de rede, no mesmo formato escrito por <see cref="FrameWriter"/>.
/// </summary>
public static class FrameReader
{
    private const int HeaderSize = 4 + 1 + 1;

    /// <summary>
    /// Payload de controle é sempre JSON pequeno (metadados, nunca os dados do job em si).
    /// Um valor muito maior que isso indica frame corrompido ou peer malicioso.
    /// </summary>
    public const int MaxControlPayloadSize = 1024 * 1024;

    public static async Task<MessageEnvelope> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        await FrameIO.ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        if (payloadLength < 0 || payloadLength > MaxControlPayloadSize)
        {
            throw new ProtocolException($"Tamanho de payload inválido: {payloadLength} bytes.");
        }

        byte version = header[4];
        byte rawType = header[5];
        if (!Enum.IsDefined(typeof(MessageType), rawType))
        {
            throw new ProtocolException($"Tipo de mensagem desconhecido: {rawType}.");
        }

        byte[] payload = payloadLength == 0 ? Array.Empty<byte>() : new byte[payloadLength];
        if (payloadLength > 0)
        {
            await FrameIO.ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        }

        return new MessageEnvelope(version, (MessageType)rawType, payload);
    }

    public static T ReadMessage<T>(MessageEnvelope envelope)
    {
        return JsonSerializer.Deserialize<T>(envelope.Payload)
            ?? throw new ProtocolException($"Payload de {envelope.Type} desserializou como nulo.");
    }

    /// <summary>
    /// Lê exatamente <paramref name="length"/> bytes brutos do stream (sem envelope de frame)
    /// e repassa cada bloco lido para <paramref name="onChunk"/>, sem acumular o job inteiro em memória.
    /// </summary>
    public static async Task ReadRawAsync(Stream stream, long length, Func<ReadOnlyMemory<byte>, CancellationToken, Task> onChunk, CancellationToken cancellationToken = default, int bufferSize = 81920)
    {
        await FrameIO.CopyExactAsync(stream, length, onChunk, cancellationToken, bufferSize).ConfigureAwait(false);
    }
}
