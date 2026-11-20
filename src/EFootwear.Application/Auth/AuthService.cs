using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Auth;

public sealed record LoginRequest(string? Company, string? UserName, string? Password, long? StoreId);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record LoginResult(string Token, bool MustChangePassword, Row Me);

/// <summary>
/// Sign-in and session handling. The database owns the rules (sec.login, sec.validate_session: lockout, idle timeout,
/// revocation, attempt log); the API only verifies the password hash, which the database never sees in clear.
/// The session token is "{companyId}.{secret}" so one lookup can set the tenant scope before row-level security applies.
/// </summary>
public sealed class AuthService(IUnitOfWorkFactory uows, IPasswordHasher hasher, SecurityOptions options)
{
    // Verified against when the user does not exist, so response time does not reveal which usernames are real.
    private readonly string _dummyHash = hasher.Hash("not-a-real-password");

    public async Task<LoginResult> LoginAsync(LoginRequest req, string? ip, string? device, string? userAgent, CancellationToken ct)
    {
        new Check().NotBlank(req.Company, "company", "Company").NotBlank(req.UserName, "userName", "User name")
            .NotBlank(req.Password, "password", "Password").ThrowIfAny();

        long companyId;
        await using (var anon = await uows.BeginAnonymousAsync(ct))
        {
            var company = await anon.Auth.FindCompanyAsync(req.Company!.Trim(), ct);
            if (company is null) throw new UnauthorizedAppException("Invalid company, user name or password.");
            companyId = company.Get<long>("id");
        }

        await using var uow = await uows.BeginForCompanyAsync(companyId, ct);
        var user = await uow.Auth.FindUserForLoginAsync(companyId, req.UserName!.Trim(), ct);
        var ok = hasher.Verify(req.Password!, user?.Get<string>("passwordHash") ?? _dummyHash) && user is not null;

        var res = await uow.Auth.LoginAsync(companyId, req.UserName.Trim(), ok, ip, device, userAgent, ct);
        await uow.CommitAsync(ct);   // failed attempts and lockouts must persist

        switch (res.Get<string>("outcome"))
        {
            case "OK": break;
            case "LOCKED":
                throw new UnauthorizedAppException($"Too many failed attempts. Try again after {res.Get<DateTimeOffset>("lockedUntil"):HH:mm}.");
            default:
                throw new UnauthorizedAppException("Invalid company, user name or password.");
        }

        var token = $"{companyId}.{res.Get<string>("sessionToken")}";
        var ctx = await BuildContextAsync(companyId, res.Get<long>("userId"), req.StoreId, ip, device, ct);
        return new LoginResult(token, res.Get<bool>("mustChangePassword"), await MeAsync(ctx, ct));
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        if (!TryParse(token, out var companyId, out var secret)) return;
        await using var uow = await uows.BeginForCompanyAsync(companyId, ct);
        await uow.Auth.LogoutAsync(companyId, secret, ct);
        await uow.CommitAsync(ct);
    }

    /// <summary>Called on every request: validates the session (sliding idle timeout) and rebuilds the caller's context.</summary>
    public async Task<UserContext> AuthenticateAsync(string token, long? requestedStore, string? ip, string? device, CancellationToken ct)
    {
        if (!TryParse(token, out var companyId, out var secret)) throw new UnauthorizedAppException("Session is not valid.");
        long userId;
        await using (var uow = await uows.BeginForCompanyAsync(companyId, ct))
        {
            userId = await uow.Auth.ValidateSessionAsync(companyId, secret, ct);
            await uow.CommitAsync(ct);
        }
        return await BuildContextAsync(companyId, userId, requestedStore, ip, device, ct);
    }

    public async Task<UserContext> BuildContextAsync(long companyId, long userId, long? requestedStore, string? ip, string? device, CancellationToken ct)
    {
        await using var uow = await uows.BeginForCompanyAsync(companyId, ct);
        var u = await uow.Auth.LoadUserAsync(companyId, userId, ct) ?? throw new UnauthorizedAppException("User not found.");
        var isOwner = u.Get<string>("roleCode") == RoleTemplates.Owner;
        var allStores = u.Get<bool>("allStores");
        var stores = await uow.Auth.LoadStoresAsync(companyId, userId, allStores, ct);
        var ids = stores.Select(s => s.Get<long>("id")).ToHashSet();
        var perms = isOwner ? (await uow.Auth.AllPermissionCodesAsync(ct)) : await uow.Auth.LoadPermissionsAsync(companyId, userId, ct);

        var selling = ids.Where(i => i > 0).OrderBy(i => i).ToList();
        var store = requestedStore is { } r && ids.Contains(r) ? r
                  : requestedStore is not null ? throw new ForbiddenException("You are not assigned to that store.")
                  : selling.Count > 0 ? selling[0] : ids.OrderBy(i => i).FirstOrDefault();

        return new UserContext(userId, companyId, u.Get<string>("username")!, u.Get<string>("fullName")!, u.Get<string>("roleCode")!,
            isOwner, allStores, ids, store, perms.ToHashSet(), u.Get<bool>("mustChangePassword"), ip, device);
    }

    /// <summary>The "who am I" payload the front end builds its menus and store switcher from.</summary>
    public async Task<Row> MeAsync(UserContext user, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(user, ct);
        var stores = await uow.Auth.LoadStoresAsync(user.CompanyId, user.UserId, user.AllStores, ct);
        var company = (await uow.Auth.FindCompanyAsync(user.CompanyId.ToString(), ct))!;
        return new Row
        {
            ["userId"] = user.UserId, ["userName"] = user.UserName, ["fullName"] = user.FullName, ["role"] = user.RoleCode,
            ["isOwner"] = user.IsOwner, ["mustChangePassword"] = user.MustChangePassword,
            ["permissions"] = user.IsOwner ? user.Permissions.OrderBy(p => p).ToList() : user.Permissions.OrderBy(p => p).ToList(),
            ["company"] = new Row { ["id"] = company["id"], ["name"] = company["name"], ["currency"] = company["currencyCode"], ["roundingMode"] = company["roundingMode"], ["timezone"] = company["timezone"] },
            ["stores"] = stores, ["currentStore"] = stores.FirstOrDefault(s => s.Get<long>("id") == user.StoreId),
        };
    }

    public async Task ChangePasswordAsync(UserContext user, ChangePasswordRequest req, CancellationToken ct)
    {
        var c = new Check().NotBlank(req.CurrentPassword, "currentPassword", "Current password").NotBlank(req.NewPassword, "newPassword", "New password");
        c.ThrowIfAny();
        ValidatePassword(req.NewPassword!);
        await using var uow = await uows.BeginAsync(user, ct);
        var row = await uow.Auth.FindUserForLoginAsync(user.CompanyId, user.UserName, ct);
        var ok = row is not null && hasher.Verify(req.CurrentPassword!, row.Get<string>("passwordHash")!);
        if (!ok) throw new ValidationException("currentPassword", "Current password is wrong.");
        if (hasher.Verify(req.NewPassword!, row!.Get<string>("passwordHash")!)) throw new ValidationException("newPassword", "Choose a password different from the current one.");
        await uow.Auth.ChangePasswordAsync(user.CompanyId, user.UserId, true, hasher.Hash(req.NewPassword!), ct);
        await uow.CommitAsync(ct);
    }

    public void ValidatePassword(string password)
    {
        var errors = new List<string>();
        if (password.Length < options.MinPasswordLength) errors.Add($"at least {options.MinPasswordLength} characters");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) errors.Add("letters and digits");
        if (errors.Count > 0) throw new ValidationException("newPassword", "Password needs " + string.Join(" and ", errors) + ".");
    }

    private static bool TryParse(string token, out long companyId, out string secret)
    {
        companyId = 0; secret = "";
        var i = token.IndexOf('.');
        if (i <= 0 || !long.TryParse(token.AsSpan(0, i), out companyId)) return false;
        secret = token[(i + 1)..];
        return secret.Length >= 32;
    }
}
