namespace PrintTool.Common.Protocol;

/// <summary>
/// Primitivas de I/O usadas por <see cref="FrameReader"/> e <see cref="FrameWriter"/>.
/// </summary>
internal static class FrameIO
{
    public static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new ProtocolException("Conexão encerrada antes do fim esperado do frame.");
            }
            offset += read;
        }
    }

    public static async Task CopyExactAsync(Stream source, Stream destination, long length, CancellationToken cancellationToken, int bufferSize)
    {
        byte[] buffer = new byte[bufferSize];
        long remaining = length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(remaining, buffer.Length);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new ProtocolException("Conexão encerrada antes do fim esperado dos dados do job.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    public static async Task CopyExactAsync(Stream source, long length, Func<ReadOnlyMemory<byte>, CancellationToken, Task> onChunk, CancellationToken cancellationToken, int bufferSize)
    {
        byte[] buffer = new byte[bufferSize];
        long remaining = length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(remaining, buffer.Length);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new ProtocolException("Conexão encerrada antes do fim esperado dos dados do job.");
            }
            await onChunk(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
