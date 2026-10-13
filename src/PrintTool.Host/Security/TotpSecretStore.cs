using System.Security.Cryptography;
using System.Text.Json;
using PrintTool.Common.Security;

namespace PrintTool.Host.Security;

/// <summary>
/// Segredo TOTP (RFC 6238) do Host: gerado uma única vez, na primeira execução, e persistido
/// localmente. É o mesmo segredo escaneado pelo administrador no app autenticador — trocar o
/// arquivo (ou apagá-lo) invalida o QR code/código já configurado em qualquer app.
/// </summary>
public sealed class TotpSecretStore
{
    private const int SecretSizeBytes = 20; // 160 bits, o tamanho recomendado pelo RFC 4226 para HMAC-SHA1.

    public byte[] Secret { get; }

    /// <summary>Representação Base32 do segredo, usada na URI <c>otpauth://</c> e para digitação manual.</summary>
    public string SecretBase32 => Base32.Encode(Secret);

    private TotpSecretStore(byte[] secret)
    {
        Secret = secret;
    }

    public static TotpSecretStore LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            TotpSecretData? data = JsonSerializer.Deserialize<TotpSecretData>(json);
            if (data is not null && !string.IsNullOrWhiteSpace(data.SecretBase32))
            {
                return new TotpSecretStore(Base32.Decode(data.SecretBase32));
            }
        }

        byte[] secret = RandomNumberGenerator.GetBytes(SecretSizeBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(new TotpSecretData(Base32.Encode(secret))));
        return new TotpSecretStore(secret);
    }

    private sealed record TotpSecretData(string SecretBase32);
}
