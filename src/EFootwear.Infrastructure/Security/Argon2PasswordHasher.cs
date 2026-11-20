using System.Security.Cryptography;
using System.Text;
using EFootwear.Application.Abstractions;
using Konscious.Security.Cryptography;

namespace EFootwear.Infrastructure.Security;

/// <summary>Argon2id (Part 6: strong password hashing). Format: argon2id$memoryKiB$iterations$parallelism$salt$hash.</summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int MemoryKib = 19456, Iterations = 2, Parallelism = 1, SaltBytes = 16, HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKib, Iterations, Parallelism);
        return $"argon2id${MemoryKib}${Iterations}${Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 6 || p[0] != "argon2id") return false;
        try
        {
            var expected = Convert.FromBase64String(p[5]);
            var actual = Compute(password, Convert.FromBase64String(p[4]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Compute(string password, byte[] salt, int memory, int iterations, int parallelism)
        => new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = memory, Iterations = iterations, DegreeOfParallelism = parallelism }.GetBytes(HashBytes);
}
