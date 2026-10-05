using System.Text;
using PrintTool.Common.Security;
using Xunit;

namespace PrintTool.Common.Tests.Security;

public class Base32Tests
{
    // Vetores de teste conhecidos do RFC 4648 (Seção 10).
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_RfcTestVectors_MatchesKnownOutput(string input, string expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);

        string encoded = Base32.Encode(data);

        Assert.Equal(expected, encoded);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY", "f")]
    [InlineData("MZXQ", "fo")]
    [InlineData("MZXW6", "foo")]
    [InlineData("MZXW6YQ", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI", "foobar")]
    public void Decode_RfcTestVectors_MatchesKnownOutput(string input, string expected)
    {
        byte[] decoded = Base32.Decode(input);

        Assert.Equal(Encoding.ASCII.GetBytes(expected), decoded);
    }

    [Fact]
    public void Decode_LowercaseAndPadding_IsAcceptedCaseInsensitively()
    {
        byte[] decoded = Base32.Decode("mzxw6ytboi======");

        Assert.Equal(Encoding.ASCII.GetBytes("foobar"), decoded);
    }

    [Fact]
    public void Decode_InvalidCharacter_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("1nvalid!"));
    }

    [Fact]
    public void RandomBytes_RoundTripsThroughEncodeAndDecode()
    {
        byte[] original = new byte[20];
        new Random(7).NextBytes(original);

        string encoded = Base32.Encode(original);
        byte[] decoded = Base32.Decode(encoded);

        Assert.Equal(original, decoded);
    }
}
