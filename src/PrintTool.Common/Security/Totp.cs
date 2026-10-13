using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PrintTool.Common.Security;

/// <summary>
/// TOTP (RFC 6238) sobre HMAC-SHA1, o mesmo algoritmo usado por Google/Microsoft Authenticator
/// e Authy na opção "Outra conta". Período fixo de 30 segundos, código de 6 dígitos.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private const int Digits = 6;

    public static string GenerateCode(ReadOnlySpan<byte> secret, DateTimeOffset timestamp)
    {
        long counter = CounterFor(timestamp);
        return ComputeCode(secret, counter);
    }

    /// <summary>
    /// Valida <paramref name="code"/> contra o contador atual e, para tolerar relógios
    /// levemente dessincronizados entre Host e Client, os <paramref name="window"/> passos
    /// imediatamente antes e depois.
    /// </summary>
    public static bool ValidateCode(ReadOnlySpan<byte> secret, string code, DateTimeOffset now, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        long counter = CounterFor(now);
        for (int offset = -window; offset <= window; offset++)
        {
            string candidate = ComputeCode(secret, counter + offset);
            if (FixedTimeEquals(candidate, code))
            {
                return true;
            }
        }

        return false;
    }

    private static long CounterFor(DateTimeOffset timestamp) => timestamp.ToUnixTimeSeconds() / StepSeconds;

    private static string ComputeCode(ReadOnlySpan<byte> secret, long counter)
    {
        byte[] counterBytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        byte[] hash = HMACSHA1.HashData(secret, counterBytes);

        int offset = hash[^1] & 0x0F;
        int truncated =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        int code = truncated % (int)Math.Pow(10, Digits);
        return code.ToString(new string('0', Digits));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(a),
            System.Text.Encoding.ASCII.GetBytes(b));
    }
}
