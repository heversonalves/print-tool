using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;
using Xunit;

namespace PrintTool.Client.Tests.TestSupport;

/// <summary>
/// Monta, nos testes, o lado "Host" mínimo que o <see cref="PrintTool.Client.Forwarding.JobForwarder"/>
/// real espera encontrar: TLS com certificado autoassinado, seguido de um <c>AuthenticateRequest</c>
/// antes de qualquer frame de job. Evita repetir esse boilerplate em cada teste de integração do Client.
/// </summary>
internal static class FakeTlsHost
{
    public static X509Certificate2 CreateSelfSignedCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PrintTool.Tests-Host", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    public static string ThumbprintOf(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    /// <summary>Aceita a próxima conexão do listener e completa o handshake TLS do lado servidor.</summary>
    public static async Task<SslStream> AcceptAndAuthenticateAsync(TcpListener listener, X509Certificate2 certificate)
    {
        TcpClient accepted = await listener.AcceptTcpClientAsync();
        var sslStream = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(certificate);
        return sslStream;
    }

    /// <summary>Lê o <c>AuthenticateRequest</c> esperado e responde o resultado indicado.</summary>
    public static async Task ExpectAuthenticateRequestAsync(Stream stream, bool approve = true)
    {
        MessageEnvelope envelope = await FrameReader.ReadFrameAsync(stream);
        Assert.Equal(MessageType.AuthenticateRequest, envelope.Type);
        await FrameWriter.WriteMessageAsync(stream, MessageType.AuthenticateResult, new AuthenticateResult(approve));
    }
}
