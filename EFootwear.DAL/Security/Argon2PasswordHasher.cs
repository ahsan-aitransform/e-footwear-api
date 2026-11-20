using System.Security.Cryptography;
using System.Text;
using EFootwear.BLL.Abstractions;
using Konscious.Security.Cryptography;

namespace EFootwear.DAL.Security;

/// <summary>
/// Argon2id. Stored form: $argon2id$v=19$m=19456,t=2,p=1$salt$hash (base64).
/// The parameters are read back from the stored value, so they can be raised later without breaking old hashes.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int MemoryKb = 19456;   // OWASP minimum for Argon2id: 19 MiB, 2 iterations, 1 lane
    private const int Iterations = 2;
    private const int Lanes = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly Lazy<string> DummyHash = new(() => new Argon2PasswordHasher().Hash("dummy-password"));

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKb, Iterations, Lanes, HashBytes);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Lanes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (!TryParse(encodedHash, out var m, out var t, out var p, out var salt, out var expected)) return false;
        var actual = Compute(password, salt, m, t, p, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public void BurnTime(string password) => Verify(password, DummyHash.Value);

    private static byte[] Compute(string password, byte[] salt, int memoryKb, int iterations, int lanes, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = lanes,
        };
        return argon.GetBytes(length);
    }

    private static bool TryParse(string s, out int m, out int t, out int p, out byte[] salt, out byte[] hash)
    {
        m = t = p = 0; salt = hash = [];
        var parts = s.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id") return false;
        foreach (var kv in parts[2].Split(','))
        {
            var pair = kv.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], out var v)) return false;
            switch (pair[0]) { case "m": m = v; break; case "t": t = v; break; case "p": p = v; break; }
        }
        if (m <= 0 || t <= 0 || p <= 0) return false;
        try { salt = Convert.FromBase64String(parts[3]); hash = Convert.FromBase64String(parts[4]); }
        catch (FormatException) { return false; }
        return salt.Length > 0 && hash.Length > 0;
    }
}
