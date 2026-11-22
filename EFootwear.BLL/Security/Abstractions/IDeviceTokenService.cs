using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>Creates device tokens: long random secrets whose SHA-256 hash is what gets stored.</summary>
public interface IDeviceTokenService
{
    GeneratedDeviceToken Generate();
}
