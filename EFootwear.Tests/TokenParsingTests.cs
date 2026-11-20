using EFootwear.BLL.Auth;

namespace EFootwear.Tests;

public class TokenParsingTests
{
    [Fact]
    public void Valid_token_is_split_into_company_and_secret()
    {
        Assert.True(AuthService.TryParseToken("12.abcdef", out var company, out var secret));
        Assert.Equal(12, company);
        Assert.Equal("abcdef", secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData(".abcdef")]
    [InlineData("12.")]
    [InlineData("0.abc")]
    [InlineData("-5.abc")]
    [InlineData("x.abc")]
    public void Malformed_token_is_rejected(string? raw)
    {
        Assert.False(AuthService.TryParseToken(raw, out _, out _));
    }
}
