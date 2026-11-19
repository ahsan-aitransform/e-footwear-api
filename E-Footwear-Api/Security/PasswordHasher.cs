using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace E_Footwear_Api.Security;

/// <summary>Argon2id password hashing. Format: argon2id$m$t$p$saltB64$hashB64. Never log or return hashes.</summary>
public static class PasswordHasher
{
    private const int Memory = 19456, Iterations = 2, Parallelism = 1, SaltLen = 16, HashLen = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var hash = Compute(password, salt, Memory, Iterations, Parallelism);
        return $"argon2id${Memory}${Iterations}${Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 6 || p[0] != "argon2id") return false;
        if (!int.TryParse(p[1], out var m) || !int.TryParse(p[2], out var t) || !int.TryParse(p[3], out var par)) return false;
        var expected = Convert.FromBase64String(p[5]);
        var actual = Compute(password, Convert.FromBase64String(p[4]), m, t, par);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Compute(string password, byte[] salt, int m, int t, int par)
    {
        using var a = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        { Salt = salt, MemorySize = m, Iterations = t, DegreeOfParallelism = par };
        return a.GetBytes(HashLen);
    }
}
