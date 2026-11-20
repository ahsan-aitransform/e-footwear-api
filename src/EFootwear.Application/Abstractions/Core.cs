using EFootwear.Domain.Common;

namespace EFootwear.Application.Abstractions;

/// <summary>
/// Everything the server knows about the caller. It is rebuilt from the database on every request (never read from the
/// client), so blocking a user or removing a permission takes effect on the next call.
/// </summary>
public sealed record UserContext(
    long UserId,
    long CompanyId,
    string UserName,
    string FullName,
    string RoleCode,
    bool IsOwner,
    bool AllStores,
    IReadOnlySet<long> StoreIds,
    long StoreId,
    IReadOnlySet<string> Permissions,
    bool MustChangePassword,
    string? Ip = null,
    string? Device = null)
{
    public bool Has(string permission) => IsOwner || Permissions.Contains(permission);
    public bool CanUseStore(long storeId) => AllStores || StoreIds.Contains(storeId);

    public void Require(string permission)
    {
        if (!Has(permission)) throw new ForbiddenException($"Missing permission '{permission}'.");
    }

    public void RequireAny(params string[] permissions)
    {
        if (!permissions.Any(Has)) throw new ForbiddenException($"Missing permission '{string.Join("' or '", permissions)}'.");
    }

    public void RequireStore(long storeId)
    {
        if (!CanUseStore(storeId)) throw new ForbiddenException("You are not assigned to this store.");
    }
}

public interface ICurrentUser
{
    UserContext User { get; }
    bool IsAuthenticated { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed class SecurityOptions
{
    public int MinPasswordLength { get; set; } = 8;
}

/// <summary>A read-model row: camelCase keys, values straight from the database.</summary>
public sealed class Row : Dictionary<string, object?>
{
    public Row() { }
    public Row(IDictionary<string, object?> src) : base(src) { }

    public T? Get<T>(string key)
    {
        if (!TryGetValue(key, out var v) || v is null or DBNull) return default;
        var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (t.IsInstanceOfType(v)) return (T)v;
        if (t == typeof(DateTimeOffset) && v is DateTime dt)
            return (T)(object)new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        if (t == typeof(DateOnly) && v is DateTime d2) return (T)(object)DateOnly.FromDateTime(d2);
        if (t == typeof(string)) return (T)(object)v.ToString()!;
        return (T)Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNo, int PageSize);

public sealed record PageQuery(int Page = 1, int PageSize = 50, string? Search = null, string? Sort = null, bool Desc = false)
{
    public int Offset => (Math.Max(Page, 1) - 1) * Limit;
    public int Limit => Math.Clamp(PageSize, 1, 500);
}
