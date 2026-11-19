using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace E_Footwear_Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "e-footwear-api";
    public string Audience { get; set; } = "e-footwear";
    public string Key { get; set; } = "";               // from environment / secret store, >= 32 bytes
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;
}

public static class Claims
{
    public const string UserId = "uid";
    public const string CompanyId = "cid";
    public const string Kind = "kind";      // "tenant" | "platform"
}

public sealed class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> opt)
{
    private readonly JwtOptions _o = opt.Value;

    public string CreateAccessToken(long userId, long? companyId, string kind)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var claims = new List<Claim> { new(Claims.UserId, userId.ToString()), new(Claims.Kind, kind) };
        if (companyId is { } c) claims.Add(new Claim(Claims.CompanyId, c.ToString()));
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_o.AccessMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static (string token, string hash) NewRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public DateTime RefreshExpiry() => DateTime.UtcNow.AddDays(_o.RefreshDays);
    public int AccessSeconds => _o.AccessMinutes * 60;
}
