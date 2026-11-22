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
    private const int MemorySizeInKibibytes = 19456;   // OWASP minimum for Argon2id: 19 MiB, 2 iterations, 1 lane
    private const int IterationCount = 2;
    private const int ParallelLaneCount = 1;
    private const int SaltLengthInBytes = 16;
    private const int HashLengthInBytes = 32;
    private const string AlgorithmName = "argon2id";
    private const int EncodedPartCount = 5;
    private const char ParameterSeparator = '$';
    private const string DummyPassword = "dummy-password";

    private static readonly Lazy<string> DummyHash = new(() => new Argon2PasswordHasher().Hash(DummyPassword));

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthInBytes);
        var hash = ComputeHash(password, salt, MemorySizeInKibibytes, IterationCount, ParallelLaneCount, HashLengthInBytes);
        return $"${AlgorithmName}$v=19$m={MemorySizeInKibibytes},t={IterationCount},p={ParallelLaneCount}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (!TryParseEncodedHash(encodedHash, out var memorySize, out var iterations, out var lanes, out var salt, out var expectedHash))
            return false;
        var actualHash = ComputeHash(password, salt, memorySize, iterations, lanes, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public void BurnTime(string password) => Verify(password, DummyHash.Value);

    private static byte[] ComputeHash(string password, byte[] salt, int memorySize, int iterations, int lanes, int hashLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memorySize,
            Iterations = iterations,
            DegreeOfParallelism = lanes,
        };
        return argon2.GetBytes(hashLength);
    }

    private static bool TryParseEncodedHash(string encodedHash, out int memorySize, out int iterations, out int lanes,
                                            out byte[] salt, out byte[] hash)
    {
        memorySize = iterations = lanes = 0;
        salt = hash = [];
        var parts = encodedHash.Split(ParameterSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != EncodedPartCount || parts[0] != AlgorithmName) return false;
        foreach (var parameter in parts[2].Split(','))
        {
            var nameAndValue = parameter.Split('=');
            if (nameAndValue.Length != 2 || !int.TryParse(nameAndValue[1], out var value)) return false;
            switch (nameAndValue[0])
            {
                case "m": memorySize = value; break;
                case "t": iterations = value; break;
                case "p": lanes = value; break;
            }
        }
        if (memorySize <= 0 || iterations <= 0 || lanes <= 0) return false;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException) { return false; }
        return salt.Length > 0 && hash.Length > 0;
    }
}
