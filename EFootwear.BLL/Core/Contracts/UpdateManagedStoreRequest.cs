namespace EFootwear.BLL.Core.Contracts;

/// <summary>The store code never changes after creation (it identifies the store on documents and in reports).</summary>
public sealed record UpdateManagedStoreRequest(string Name, string? Address, string? Phone, long ExpectedRowVersion);
