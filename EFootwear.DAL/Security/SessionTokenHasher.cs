using System.Security.Cryptography;
using System.Text;

namespace EFootwear.DAL.Security;

/// <summary>
/// SHA-256 of a token as lower-case hex: exactly what the database stores (<c>sec.token_hash</c>). Lets the API recognise the caller's
/// own session without ever reading a token back from the database.
/// </summary>
public static class SessionTokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
