using EFootwear.BLL.Security.Services;

namespace EFootwear.Tests.Security;

public class TokenParsingTests
{
    private static readonly Guid ExampleCompanyId = Guid.Parse("01a1f987-7abe-76ff-bf2f-60450b67f018");

    [Fact]
    public void Valid_token_is_split_into_company_and_secret()
    {
        Assert.True(UserAuthenticationService.TryParseToken($"{ExampleCompanyId}.abcdef", out var companyId, out var secret));
        Assert.Equal(ExampleCompanyId, companyId);
        Assert.Equal("abcdef", secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData(".abcdef")]
    [InlineData("12.")]
    [InlineData("12.abcdef")]                                          // a number is not a company id any more
    [InlineData("00000000-0000-0000-0000-000000000000.abc")]           // the nil uuid is never a company
    [InlineData("x.abc")]
    public void Malformed_token_is_rejected(string? rawToken)
    {
        Assert.False(UserAuthenticationService.TryParseToken(rawToken, out _, out _));
    }
}
