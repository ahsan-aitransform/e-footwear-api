using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class AuthRepository(Db db) : IAuthRepository
{
    public Task<Row?> FindCompanyAsync(string idOrName, CancellationToken ct)
        => db.RowAsync(
            @"select id, name, currency_code, rounding_mode, timezone from platform.companies
               where status = 'ACTIVE' and (id::text = @k or lower(name) = lower(@k))
               order by (id::text = @k) desc limit 1", new { k = idOrName }, ct);

    public Task<Row?> FindUserForLoginAsync(long companyId, string username, CancellationToken ct)
        => db.RowAsync("select id, password_hash from sec.users where company_id = @c and lower(username) = lower(@u) and not is_deleted",
                       new { c = companyId, u = username }, ct);

    public async Task<Row> LoginAsync(long companyId, string username, bool passwordOk, string? ip, string? device, string? userAgent, CancellationToken ct)
        => (await db.RowAsync(
            "select out_outcome as outcome, out_user_id as user_id, out_session_token as session_token, out_must_change_password as must_change_password, out_locked_until as locked_until " +
            "from sec.login(@c, @u, @ok, @ip::inet, @dev, @ua)",
            new { c = companyId, u = username, ok = passwordOk, ip, dev = device, ua = userAgent }, ct))!;

    public async Task<long> ValidateSessionAsync(long companyId, string token, CancellationToken ct)
    {
        try { return await db.ScalarAsync<long>("select sec.validate_session(@c, @t)", new { c = companyId, t = token }, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "28000")   // the function's "missing, expired or revoked"
        { throw new UnauthorizedAppException("Session expired. Please sign in again."); }
    }

    public Task LogoutAsync(long companyId, string token, CancellationToken ct)
        => db.ExecAsync("select sec.logout(@c, @t)", new { c = companyId, t = token }, ct);

    public Task<Row?> LoadUserAsync(long companyId, long userId, CancellationToken ct)
        => db.RowAsync(
            @"select id, username, full_name, role_code, all_stores, must_change_password, staff_party_id
                from sec.users where company_id = @c and id = @u and not is_deleted and status = 'ACTIVE'", new { c = companyId, u = userId }, ct);

    /// <summary>Stores the user may work in. Head office (id 0) is only listed for users with every store.</summary>
    public Task<List<Row>> LoadStoresAsync(long companyId, long userId, bool allStores, CancellationToken ct)
        => db.RowsAsync(allStores
            ? "select id, code, name, is_head_office from core.stores where company_id = @c and is_active order by id"
            : @"select s.id, s.code, s.name, s.is_head_office from core.stores s
                  join sec.user_stores us on us.company_id = s.company_id and us.store_id = s.id
                 where s.company_id = @c and us.user_id = @u and s.is_active order by s.id", new { c = companyId, u = userId }, ct);

    public Task<List<string>> LoadPermissionsAsync(long companyId, long userId, CancellationToken ct)
        => db.QueryAsync<string>("select permission_code from sec.user_permissions where company_id = @c and user_id = @u order by 1", new { c = companyId, u = userId }, ct);

    public Task<List<string>> AllPermissionCodesAsync(CancellationToken ct)
        => db.QueryAsync<string>("select code from sec.permissions order by 1", null, ct);

    public Task ChangePasswordAsync(long companyId, long userId, bool currentOk, string newHash, CancellationToken ct)
        => db.ExecAsync("select sec.change_password(@c, @u, @ok, @h)", new { c = companyId, u = userId, ok = currentOk, h = newHash }, ct);
}
