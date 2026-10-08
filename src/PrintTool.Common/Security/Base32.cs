namespace PrintTool.Common.Security;

/// <summary>
/// Codificação Base32 (RFC 4648), sem padding na saída. É o formato usado pelos apps
/// autenticadores (Google/Microsoft Authenticator, Authy) para exibir e aceitar segredos TOTP.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                int index = (buffer >> bitsInBuffer) & 0x1F;
                builder.Append(Alphabet[index]);
            }
        }

        if (bitsInBuffer > 0)
        {
            int index = (buffer << (5 - bitsInBuffer)) & 0x1F;
            builder.Append(Alphabet[index]);
        }

        return builder.ToString();
    }

    public static byte[] Decode(string base32)
    {
        string normalized = base32.Trim().TrimEnd('=').ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var output = new MemoryStream((normalized.Length * 5 + 7) / 8);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (char c in normalized)
        {
            int index = Alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new FormatException($"Caractere inválido para Base32: '{c}'.");
            }

            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                output.WriteByte((byte)((buffer >> bitsInBuffer) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
