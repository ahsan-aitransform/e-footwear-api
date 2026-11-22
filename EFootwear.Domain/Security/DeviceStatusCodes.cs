namespace EFootwear.Domain.Security;

/// <summary>
/// The authorization state of a POS device (Part 6 device authorization, Part 23.7). A device is registered first (Pending), then a
/// person with the approve permission authorizes it; an authorized device can be revoked at any time. Derived from core.devices.
/// </summary>
public static class DeviceStatusCodes
{
    public const string Pending = "PENDING";
    public const string Authorized = "AUTHORIZED";
    public const string Revoked = "REVOKED";
}
