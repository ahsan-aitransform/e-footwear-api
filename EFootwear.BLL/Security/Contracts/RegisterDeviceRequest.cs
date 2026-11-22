namespace EFootwear.BLL.Security.Contracts;

/// <summary>Registers a POS device for a store. <paramref name="DeviceUuid"/> is the id of the device itself; the server creates one when it is empty.</summary>
public sealed record RegisterDeviceRequest(Guid StoreId, string Label, Guid? DeviceUuid);
