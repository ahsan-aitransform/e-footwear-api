namespace EFootwear.Tests.Infrastructure;

/// <summary>A company created through the platform administrator API, with its first store and its owner's temporary password.</summary>
public sealed record TestCompany(Guid CompanyId, Guid FirstStoreId, string OwnerEmail, string OwnerTemporaryPassword);
