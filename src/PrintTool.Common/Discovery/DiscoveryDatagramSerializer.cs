using System.Text.Json;

namespace PrintTool.Common.Discovery;

/// <summary>
/// Codifica e decodifica os datagramas UDP de descoberta, usados tanto pelo Host quanto
/// pelo Client. Formato: [Magic (4 bytes)][kind (1 byte)][payload JSON em UTF-8].
/// </summary>
public static class DiscoveryDatagramSerializer
{
    public static byte[] Serialize<T>(DiscoveryDatagramKind kind, T message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message);
        byte[] buffer = new byte[DiscoveryConstants.Magic.Length + 1 + json.Length];

        DiscoveryConstants.Magic.CopyTo(buffer, 0);
        buffer[DiscoveryConstants.Magic.Length] = (byte)kind;
        json.CopyTo(buffer, DiscoveryConstants.Magic.Length + 1);

        return buffer;
    }

    /// <summary>
    /// Tenta decodificar um datagrama recebido. Retorna <c>false</c> silenciosamente para
    /// qualquer pacote sem o prefixo esperado — é tráfego de rede alheio ao protocolo, não um erro.
    /// </summary>
    public static bool TryDeserialize(ReadOnlySpan<byte> datagram, out DiscoveryDatagram? result)
    {
        result = null;

        int headerLength = DiscoveryConstants.Magic.Length + 1;
        if (datagram.Length < headerLength)
        {
            return false;
        }

        if (!datagram[..DiscoveryConstants.Magic.Length].SequenceEqual(DiscoveryConstants.Magic))
        {
            return false;
        }

        byte rawKind = datagram[DiscoveryConstants.Magic.Length];
        if (!Enum.IsDefined(typeof(DiscoveryDatagramKind), rawKind))
        {
            return false;
        }

        byte[] payload = datagram[headerLength..].ToArray();
        result = new DiscoveryDatagram((DiscoveryDatagramKind)rawKind, payload);
        return true;
    }

    public static T ReadMessage<T>(DiscoveryDatagram datagram)
    {
        return JsonSerializer.Deserialize<T>(datagram.JsonPayload)
            ?? throw new InvalidOperationException($"Datagrama de descoberta ({datagram.Kind}) desserializou como nulo.");
    }
}
