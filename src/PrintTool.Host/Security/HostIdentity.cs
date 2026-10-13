using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PrintTool.Host.Security;

/// <summary>
/// Identidade estável do Host: um <see cref="HostId"/> (GUID) que não muda com hostname/IP,
/// e um certificado TLS autoassinado. Ambos são gerados uma única vez, na primeira execução,
/// e reaproveitados depois — trocar qualquer um dos dois invalida o pareamento de todos os
/// Clients (o thumbprint do certificado é o que fica fixado no pareamento).
/// </summary>
public sealed class HostIdentity
{
    public Guid HostId { get; }
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// Thumbprint SHA-256 do certificado, em hexadecimal maiúsculo — o valor anunciado na
    /// descoberta e fixado pelo Client no pareamento.
    /// </summary>
    public string CertificateThumbprint => Convert.ToHexString(Certificate.GetCertHash(HashAlgorithmName.SHA256));

    private HostIdentity(Guid hostId, X509Certificate2 certificate)
    {
        HostId = hostId;
        Certificate = certificate;
    }

    public static HostIdentity LoadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);

        Guid hostId = LoadOrCreateHostId(Path.Combine(directory, "host-identity.json"));
        X509Certificate2 certificate = LoadOrCreateCertificate(Path.Combine(directory, "host-cert.pfx"), hostId);

        return new HostIdentity(hostId, certificate);
    }

    private static Guid LoadOrCreateHostId(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            HostIdentityData? data = JsonSerializer.Deserialize<HostIdentityData>(json);
            if (data is not null)
            {
                return data.HostId;
            }
        }

        var hostId = Guid.NewGuid();
        File.WriteAllText(path, JsonSerializer.Serialize(new HostIdentityData(hostId)));
        return hostId;
    }

    private static X509Certificate2 LoadOrCreateCertificate(string path, Guid hostId)
    {
        if (File.Exists(path))
        {
            return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.Exportable);
        }

        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=PrintTool.Host-{hostId:N}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Validade longa: não há CA nem renovação automática nesta fase — o certificado só
        // é substituído se o administrador apagar o arquivo (o que exige repareamento de todos os Clients).
        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(20));

        byte[] pfxBytes = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfxBytes);

        return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
    }

    private sealed record HostIdentityData(Guid HostId);
}
