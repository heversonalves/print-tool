using QRCoder;

namespace PrintTool.Host.Security;

/// <summary>
/// Monta a URI <c>otpauth://</c> do segredo TOTP do Host e grava um PNG escaneável por
/// qualquer app autenticador (Google/Microsoft Authenticator, Authy). Usa
/// <see cref="PngByteQRCode"/> (QRCoder), que não depende de System.Drawing — funciona
/// tanto no Windows Service quanto rodando o executável direto no console.
/// </summary>
public static class QrCodeWriter
{
    public static string BuildOtpAuthUri(string hostName, string secretBase32)
    {
        string label = Uri.EscapeDataString($"PrintTool:{hostName}");
        string issuer = Uri.EscapeDataString("PrintTool");
        return $"otpauth://totp/{label}?secret={secretBase32}&issuer={issuer}&digits=6&period=30";
    }

    public static void WritePng(string otpAuthUri, string pngPath)
    {
        byte[] pngBytes = GeneratePng(otpAuthUri);

        Directory.CreateDirectory(Path.GetDirectoryName(pngPath) ?? ".");
        File.WriteAllBytes(pngPath, pngBytes);
    }

    /// <summary>
    /// Gera o PNG do QR code em memória, sem gravar em disco — usado pelo app de
    /// administração (Host.UI) para mostrar o QR code direto na tela.
    /// </summary>
    public static byte[] GeneratePng(string otpAuthUri)
    {
        var generator = new QRCodeGenerator();
        QRCodeData data = generator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.Q);
        var pngCode = new PngByteQRCode(data);
        return pngCode.GetGraphic(20);
    }
}
