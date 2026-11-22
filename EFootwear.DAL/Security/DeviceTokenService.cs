using System.Security.Cryptography;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;

namespace EFootwear.DAL.Security;

/// <summary>A device token is 32 random bytes (256 bits) as hex. Only its SHA-256 hash is stored, so a stolen database cannot be used to sign in a device.</summary>
public sealed class DeviceTokenService : IDeviceTokenService
{
    private const int TokenByteCount = 32;

    public GeneratedDeviceToken Generate()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenByteCount));
        return new GeneratedDeviceToken(token, SessionTokenHasher.Hash(token));
    }
}
