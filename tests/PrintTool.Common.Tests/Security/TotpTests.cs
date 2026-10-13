using System.Text;
using PrintTool.Common.Security;
using Xunit;

namespace PrintTool.Common.Tests.Security;

public class TotpTests
{
    // Segredo de teste padrão do RFC 4226 (Apêndice D) / RFC 6238 (Apêndice B): "12345678901234567890" em ASCII.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    // (contador HOTP, código esperado) — RFC 4226 Apêndice D. Cada contador corresponde ao
    // instante contador*30s desde a época Unix, já que o TOTP (RFC 6238) é HOTP com esse passo.
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void GenerateCode_RfcTestVectors_MatchesKnownCodes(long counter, string expectedCode)
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(counter * 30);

        string code = Totp.GenerateCode(RfcSecret, timestamp);

        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void ValidateCode_CorrectCodeAtCurrentStep_ReturnsTrue()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(59);
        string code = Totp.GenerateCode(RfcSecret, now);

        Assert.True(Totp.ValidateCode(RfcSecret, code, now));
    }

    [Fact]
    public void ValidateCode_WrongCode_ReturnsFalse()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.False(Totp.ValidateCode(RfcSecret, "000000", now));
    }

    [Fact]
    public void ValidateCode_OneStepInThePast_StillAcceptedWithinWindow()
    {
        var generatedAt = DateTimeOffset.FromUnixTimeSeconds(30);
        var validatedAt = DateTimeOffset.FromUnixTimeSeconds(61); // um passo de 30s depois

        string code = Totp.GenerateCode(RfcSecret, generatedAt);

        Assert.True(Totp.ValidateCode(RfcSecret, code, validatedAt, window: 1));
    }

    [Fact]
    public void ValidateCode_TwoStepsAway_RejectedWithDefaultWindow()
    {
        var generatedAt = DateTimeOffset.FromUnixTimeSeconds(0);
        var validatedAt = DateTimeOffset.FromUnixTimeSeconds(90); // três passos de 30s depois

        string code = Totp.GenerateCode(RfcSecret, generatedAt);

        Assert.False(Totp.ValidateCode(RfcSecret, code, validatedAt, window: 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ValidateCode_BlankCode_ReturnsFalse(string? code)
    {
        Assert.False(Totp.ValidateCode(RfcSecret, code!, DateTimeOffset.UtcNow));
    }
}
