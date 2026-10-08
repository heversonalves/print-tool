using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PrintTool.Client.Security;

/// <summary>
/// Validação de certificado TLS por fixação de thumbprint (TOFU), em vez de cadeia/CA —
/// não há PKI nesta fase. O Host é autoassinado; o que importa é que o certificado
/// apresentado seja exatamente o mesmo fixado no momento do pareamento daquele Host.
/// </summary>
public static class PinnedCertificateValidator
{
    public static bool Matches(X509Certificate? presentedCertificate, string expectedThumbprint)
    {
        if (presentedCertificate is null)
        {
            return false;
        }

        using var certificate2 = new X509Certificate2(presentedCertificate);
        string thumbprint = Convert.ToHexString(certificate2.GetCertHash(HashAlgorithmName.SHA256));
        return string.Equals(thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
    }
}
