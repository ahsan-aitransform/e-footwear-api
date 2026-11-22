using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Security.Contracts;

/// <summary>What the "new user" form may offer the caller: the roles they may create and the stores they may choose from.</summary>
public sealed record UserCreationOptionsResponse(IReadOnlyList<string> Roles, IReadOnlyList<StoreResponse> Stores,
                                                 bool NeedsStore);
