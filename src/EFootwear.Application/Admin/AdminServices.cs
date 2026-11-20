using System.Text.RegularExpressions;
using EFootwear.Application.Abstractions;
using EFootwear.Application.Auth;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Admin;

public sealed record UserInput(string? UserName, string? FullName, string? Role, string? Password, bool AllStores, long[]? StoreIds, string[]? Permissions, long? StaffPartyId);
public sealed record UserUpdate(string? FullName, string? Role, bool AllStores, long[]? StoreIds, string[]? Permissions, long? StaffPartyId);
public sealed record ResetPasswordRequest(string? TemporaryPassword);
public sealed record StatusRequest(string? Status);

/// <summary>User administration. Nobody can hand out more than they hold (no privilege escalation) and only an owner touches an owner.</summary>
public sealed class UserService(IUnitOfWorkFactory uows, IPasswordHasher hasher, AuthService auth)
{
    private static readonly string[] AssignableRoles = [RoleTemplates.Owner, RoleTemplates.Manager, RoleTemplates.Cashier, RoleTemplates.Accountant];

    public async Task<Page<Row>> ListAsync(UserContext u, PageQuery q, string? status, CancellationToken ct)
    {
        u.Require(P.UserAdmin);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Users.ListAsync(u.CompanyId, q, status, ct);
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.UserAdmin);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Users.GetAsync(u.CompanyId, id, ct) ?? throw new NotFoundException("User", id);
    }

    /// <summary>Roles, the full permission catalogue and the starting permission set of each role (editable per user afterwards).</summary>
    public async Task<Row> MetaAsync(UserContext u, CancellationToken ct)
    {
        u.Require(P.UserAdmin);
        await using var uow = await uows.BeginAsync(u, ct);
        var perms = await uow.Users.PermissionCatalogAsync(ct);
        var all = perms.Select(p => p.Get<string>("code")!).ToList();
        return new Row
        {
            ["roles"] = await uow.Users.RolesAsync(ct), ["permissions"] = perms,
            ["templates"] = AssignableRoles.ToDictionary(r => r, r => (object)(r == RoleTemplates.Owner ? all : RoleTemplates.For(r, all))),
        };
    }

    public async Task<long> CreateAsync(UserContext u, UserInput i, CancellationToken ct)
    {
        u.Require(P.UserAdmin);
        var c = new Check().NotBlank(i.UserName, "userName", "User name").NotBlank(i.FullName, "fullName", "Full name").NotBlank(i.Role, "role", "Role")
            .NotBlank(i.Password, "password", "Password").MaxLength(i.UserName, 60, "userName");
        c.Require(i.UserName is null || Regex.IsMatch(i.UserName, @"^[A-Za-z0-9._-]{3,60}$"), "userName", "Use 3–60 letters, digits, dot, dash or underscore.").ThrowIfAny();
        auth.ValidatePassword(i.Password!);
        var role = ValidRole(u, i.Role!);
        var stores = Normalise(i.StoreIds); var perms = Normalise(i.Permissions);

        await using var uow = await uows.BeginAsync(u, ct);
        await CheckGrantAsync(u, uow, role, i.AllStores, stores, perms, ct);
        var id = await uow.Users.InsertAsync(u.CompanyId, i.UserName!.Trim(), hasher.Hash(i.Password!), i.FullName!.Trim(), role, i.AllStores, i.StaffPartyId, ct);
        await uow.Users.ReplaceStoresAsync(u.CompanyId, id, stores, ct);
        await uow.Users.ReplacePermissionsAsync(u.CompanyId, id, role == RoleTemplates.Owner ? [] : perms, u.UserId, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(UserContext u, long id, UserUpdate i, CancellationToken ct)
    {
        u.Require(P.UserAdmin);
        new Check().NotBlank(i.FullName, "fullName", "Full name").NotBlank(i.Role, "role", "Role").ThrowIfAny();
        var role = ValidRole(u, i.Role!);
        var stores = Normalise(i.StoreIds); var perms = Normalise(i.Permissions);

        await using var uow = await uows.BeginAsync(u, ct);
        var existing = await uow.Users.GetAsync(u.CompanyId, id, ct) ?? throw new NotFoundException("User", id);
        var wasOwner = existing.Get<string>("roleCode") == RoleTemplates.Owner;
        if (wasOwner && !u.IsOwner) throw new ForbiddenException("Only an owner can change an owner.");
        if (id == u.UserId && !u.IsOwner) throw new ForbiddenException("You cannot change your own role or permissions.");
        if (wasOwner && role != RoleTemplates.Owner && await uow.Users.ActiveOwnerCountAsync(u.CompanyId, ct) <= 1)
            throw new BusinessRuleException("The company must keep at least one active owner.", "last_owner");

        await CheckGrantAsync(u, uow, role, i.AllStores, stores, perms, ct);
        await uow.Users.UpdateAsync(u.CompanyId, id, i.FullName!.Trim(), role, i.AllStores, i.StaffPartyId, ct);
        await uow.Users.ReplaceStoresAsync(u.CompanyId, id, stores, ct);
        await uow.Users.ReplacePermissionsAsync(u.CompanyId, id, role == RoleTemplates.Owner ? [] : perms, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task ResetPasswordAsync(UserContext u, long id, ResetPasswordRequest r, CancellationToken ct)
    {
        u.Require(P.UserPasswordReset);
        new Check().NotBlank(r.TemporaryPassword, "temporaryPassword", "Temporary password").ThrowIfAny();
        auth.ValidatePassword(r.TemporaryPassword!);
        await using var uow = await uows.BeginAsync(u, ct);
        await EnsureMayActOnAsync(u, uow, id, ct);
        await uow.Users.AdminResetPasswordAsync(u.CompanyId, id, hasher.Hash(r.TemporaryPassword!), ct);
        await uow.CommitAsync(ct);
    }

    public async Task SetStatusAsync(UserContext u, long id, StatusRequest r, CancellationToken ct)
    {
        u.Require(P.UserBlock);
        new Check().Require(r.Status is "ACTIVE" or "DISABLED", "status", "Status must be ACTIVE or DISABLED.").ThrowIfAny();
        if (id == u.UserId && r.Status == "DISABLED") throw new BusinessRuleException("You cannot block yourself.", "self_block");
        await using var uow = await uows.BeginAsync(u, ct);
        var target = await EnsureMayActOnAsync(u, uow, id, ct);
        if (r.Status == "DISABLED" && target.Get<string>("roleCode") == RoleTemplates.Owner && await uow.Users.ActiveOwnerCountAsync(u.CompanyId, ct) <= 1)
            throw new BusinessRuleException("The company must keep at least one active owner.", "last_owner");
        await uow.Users.SetStatusAsync(u.CompanyId, id, r.Status!, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> LoginHistoryAsync(UserContext u, long? userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        // Own history is always allowed; the database function enforces LOGIN_HISTORY_VIEW for everyone else's.
        return await uow.Users.LoginHistoryAsync(u.CompanyId, userId ?? u.UserId, from ?? DateTimeOffset.UtcNow.AddDays(-30), to ?? DateTimeOffset.UtcNow, ct);
    }

    private static async Task<Row> EnsureMayActOnAsync(UserContext u, IUnitOfWork uow, long id, CancellationToken ct)
    {
        var target = await uow.Users.GetAsync(u.CompanyId, id, ct) ?? throw new NotFoundException("User", id);
        if (target.Get<string>("roleCode") == RoleTemplates.Owner && !u.IsOwner) throw new ForbiddenException("Only an owner can act on an owner.");
        return target;
    }

    private static string ValidRole(UserContext u, string role)
    {
        role = role.Trim().ToUpperInvariant();
        if (!AssignableRoles.Contains(role)) throw new ValidationException("role", "Unknown role.");
        if (role == RoleTemplates.Owner && !u.IsOwner) throw new ForbiddenException("Only an owner can create or promote an owner.");
        return role;
    }

    private static long[] Normalise(long[]? v) => (v ?? []).Distinct().Order().ToArray();
    private static string[] Normalise(string[]? v) => (v ?? []).Select(x => x.Trim().ToUpperInvariant()).Distinct().Order().ToArray();

    private static async Task CheckGrantAsync(UserContext u, IUnitOfWork uow, string role, bool allStores, long[] stores, string[] perms, CancellationToken ct)
    {
        var catalogue = (await uow.Users.PermissionCatalogAsync(ct)).Select(p => p.Get<string>("code")!).ToHashSet();
        var unknown = perms.Where(p => !catalogue.Contains(p)).ToList();
        if (unknown.Count > 0) throw new ValidationException("permissions", "Unknown permission: " + string.Join(", ", unknown));
        var over = perms.Where(p => !u.Has(p)).ToList();
        if (over.Count > 0) throw new ForbiddenException("You cannot grant permissions you do not hold: " + string.Join(", ", over));
        if (allStores && !u.AllStores) throw new ForbiddenException("Only a user with every store can grant every store.");
        if (stores.Any(s => s <= 0)) throw new ValidationException("storeIds", "Choose selling stores; head office is covered by 'all stores'.");
        var foreign = stores.Where(s => !u.CanUseStore(s)).ToList();
        if (foreign.Count > 0) throw new ForbiddenException("You cannot assign stores you do not have.");
        if (role != RoleTemplates.Owner && !allStores && stores.Length == 0) throw new ValidationException("storeIds", "Assign at least one store.");
    }
}

public sealed record StoreInput(string? Code, string? Name, long? CityId, string? Address, string? Phone, bool IsActive = true, long? RowVer = null);
public sealed record CompanyInput(string? Name, string? LegalName, string? Ntn, string? Strn, string? Timezone, string? RoundingMode, string? PriceBandBasis, string? InvoiceTerms, long? RowVer);

public sealed class StoreService(IUnitOfWorkFactory uows)
{
    public async Task<List<Row>> ListAsync(UserContext u, bool includeInactive, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        var all = await uow.Stores.ListAsync(u.CompanyId, includeInactive, ct);
        return all.Where(s => u.CanUseStore(s.Get<long>("id"))).ToList();
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireStore(id);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Stores.GetAsync(u.CompanyId, id, ct) ?? throw new NotFoundException("Store", id);
    }

    public async Task<long> CreateAsync(UserContext u, StoreInput i, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        if (!u.AllStores) throw new ForbiddenException("Only a user with every store can open a new store.");
        new Check().NotBlank(i.Code, "code").NotBlank(i.Name, "name").MaxLength(i.Code, 20, "code").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Stores.InsertAsync(u.CompanyId, i.Code!.Trim().ToUpperInvariant(), i.Name!.Trim(), i.CityId, Blank(i.Address), Blank(i.Phone), ct);
        await uow.CommitAsync(ct);   // the database creates the store's stock, cash, sales, cost, loss and salary accounts
        return id;
    }

    public async Task UpdateAsync(UserContext u, long id, StoreInput i, CancellationToken ct)
    {
        u.Require(P.MasterEdit); u.RequireStore(id);
        new Check().NotBlank(i.Name, "name").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Stores.GetAsync(u.CompanyId, id, ct) ?? throw new NotFoundException("Store", id);
        if (cur.Get<bool>("isHeadOffice") && !i.IsActive) throw new BusinessRuleException("Head office cannot be closed.");
        if (!await uow.Stores.UpdateAsync(u.CompanyId, id, i.Name!.Trim(), i.CityId, Blank(i.Address), Blank(i.Phone), i.IsActive, i.RowVer, ct))
            throw new ConflictException("The store was changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task<Row> GetCompanyAsync(UserContext u, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Stores.GetCompanyAsync(u.CompanyId, ct) ?? throw new NotFoundException("Company");
    }

    public async Task UpdateCompanyAsync(UserContext u, CompanyInput i, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        if (!u.IsOwner) throw new ForbiddenException("Only an owner can change company settings.");
        var c = new Check().NotBlank(i.Name, "name", "Company name");
        c.Require(i.RoundingMode is null or "FLOOR" or "NEAREST", "roundingMode", "Rounding must be FLOOR or NEAREST.");
        c.Require(i.PriceBandBasis is null or "LIST" or "NET", "priceBandBasis", "Price band basis must be LIST or NET.");
        c.Require(i.Timezone is null || TimeZoneInfo.TryFindSystemTimeZoneById(i.Timezone, out _), "timezone", "Unknown time zone.");
        c.ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        if (!await uow.Stores.UpdateCompanyAsync(u.CompanyId, new CompanyUpdate(i.Name!.Trim(), Blank(i.LegalName), Blank(i.Ntn), Blank(i.Strn), i.Timezone, i.RoundingMode, i.PriceBandBasis, Blank(i.InvoiceTerms), i.RowVer), ct))
            throw new ConflictException("The company settings were changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> DevicesAsync(UserContext u, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        return (await uow.Stores.DevicesAsync(u.CompanyId, ct)).Where(d => u.CanUseStore(d.Get<long>("storeId"))).ToList();
    }

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record MasterBody(string? Name, string? NameUr, string? Code, int SortOrder, bool IsActive = true, long? RowVer = null);
public sealed record CategoryBody(string? Name, string? NameUr, string? Code, int SortOrder, long DepartmentId, long? ParentId, bool IsActive = true, long? RowVer = null);
public sealed record SizeBody(long SizeGroupId, string? SizeCode, int SortOrder);

public sealed class MasterService(IUnitOfWorkFactory uows)
{
    public async Task<List<Row>> ListAsync(UserContext u, string kind, string? search, bool includeInactive, CancellationToken ct)
    {
        Known(kind);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Masters.ListAsync(kind, search, includeInactive, ct);
    }

    public async Task<long> CreateAsync(UserContext u, string kind, MasterBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); Known(kind);
        Validate(b.Name, b.Code);
        await using var uow = await uows.BeginAsync(u, ct);
        if (kind == "cities") await RejectNearDuplicateCityAsync(uow, b.Name!, ct);
        var id = await uow.Masters.InsertAsync(kind, u.CompanyId, new MasterInput(b.Name!.Trim(), StoreService.Blank(b.NameUr), StoreService.Blank(b.Code), b.SortOrder), ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(UserContext u, string kind, long id, MasterBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); Known(kind);
        Validate(b.Name, b.Code);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Masters.GetAsync(kind, id, ct) is null) throw new NotFoundException("Item", id);
        if (!await uow.Masters.UpdateAsync(kind, id, new MasterInput(b.Name!.Trim(), StoreService.Blank(b.NameUr), StoreService.Blank(b.Code), b.SortOrder), b.IsActive, b.RowVer, ct))
            throw new ConflictException("This item was changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task DeleteAsync(UserContext u, string kind, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit); Known(kind);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Masters.GetAsync(kind, id, ct) is null) throw new NotFoundException("Item", id);
        await uow.Masters.SoftDeleteAsync(kind, id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> CategoriesAsync(UserContext u, string? search, long? departmentId, bool includeInactive, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Masters.CategoriesAsync(search, departmentId, includeInactive, ct);
    }

    public async Task<long> CreateCategoryAsync(UserContext u, CategoryBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); Validate(b.Name, b.Code);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Masters.InsertCategoryAsync(u.CompanyId, b.DepartmentId, b.ParentId, new MasterInput(b.Name!.Trim(), StoreService.Blank(b.NameUr), StoreService.Blank(b.Code), b.SortOrder), ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateCategoryAsync(UserContext u, long id, CategoryBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); Validate(b.Name, b.Code);
        if (b.ParentId == id) throw new ValidationException("parentId", "A category cannot be its own parent.");
        await using var uow = await uows.BeginAsync(u, ct);
        if (!await uow.Masters.UpdateCategoryAsync(id, b.DepartmentId, b.ParentId, new MasterInput(b.Name!.Trim(), StoreService.Blank(b.NameUr), StoreService.Blank(b.Code), b.SortOrder), b.IsActive, b.RowVer, ct))
            throw new ConflictException("This category was changed by someone else, or no longer exists.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task DeleteCategoryAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        await uow.Masters.SoftDeleteCategoryAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> SizesAsync(UserContext u, long? sizeGroupId, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Masters.SizesAsync(sizeGroupId, ct);
    }

    public async Task<long> CreateSizeAsync(UserContext u, SizeBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        new Check().NotBlank(b.SizeCode, "sizeCode", "Size").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Masters.InsertSizeAsync(u.CompanyId, b.SizeGroupId, b.SizeCode!.Trim(), b.SortOrder, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task DeleteSizeAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        await uow.Masters.DeleteSizeAsync(id, ct);   // a size used by a variant is refused by its foreign key (409)
        await uow.CommitAsync(ct);
    }

    private static void Known(string kind)
    {
        if (!Infrastructure_Kinds.Contains(kind)) throw new NotFoundException("Master list", kind);
    }

    // Mirrors the repository's whitelist so the service can answer 404 before SQL is built.
    private static readonly HashSet<string> Infrastructure_Kinds =
        ["departments", "brands", "seasons", "genders", "colors", "size-groups", "product-types", "units", "locations", "cities"];

    private static void Validate(string? name, string? code)
        => new Check().NotBlank(name, "name").MaxLength(name, 120, "name").MaxLength(code, 30, "code").ThrowIfAny();

    /// <summary>Cities: refuse a spelling one or two letters away from an existing city (Shiekhupura / Sheikhpura).</summary>
    private static async Task RejectNearDuplicateCityAsync(IUnitOfWork uow, string name, CancellationToken ct)
    {
        var n = name.Trim().ToLowerInvariant();
        foreach (var c in await uow.Masters.ListAsync("cities", null, true, ct))
        {
            var other = c.Get<string>("name")!.ToLowerInvariant();
            if (other != n && Distance(n, other) <= 2 && Math.Min(n.Length, other.Length) >= 5)
                throw new ConflictException($"'{c.Get<string>("name")}' already exists and looks like the same city. Use it instead.", "similar_city");
        }
    }

    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}

public sealed record PartyBody(string? Name, string? NameUr, string? FatherName, string? Cnic, string? PhonePrimary, string? PhoneSecondary, string? Email,
                               string? Address, long? CityId, bool IsCustomer, bool IsVendor, bool IsStaff, long? CreditLimit, string? Status, string? Code,
                               StaffBody? Staff, long? RowVer = null);
public sealed record StaffBody(string? Designation, bool IsManager, long BaseSalary, string? PayFrequency, long? StoreId, DateOnly? JoinedOn, DateOnly? LeftOn);
public sealed record MoveStaffBody(long StoreId, DateOnly? EffectiveOn);

public sealed class PartyService(IUnitOfWorkFactory uows)
{
    public async Task<Page<Row>> ListAsync(UserContext u, PartyFilter f, PageQuery q, CancellationToken ct)
    {
        // Vendor and staff data is not for every cashier: vendor lists need VENDOR_MANAGE or PURCHASE_CREATE, staff lists need payroll/user rights.
        if (f.Vendor == true) u.RequireAny(P.VendorManage, P.PurchaseCreate, P.PurchasePay, P.VoucherPost);
        if (f.Staff == true) u.RequireAny(P.UserAdmin, P.PayrollPrepare, P.PayrollApprove, P.MasterEdit, P.SaleCreate);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Parties.ListAsync(f, q, ct);
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        var p = await uow.Parties.GetAsync(id, ct) ?? throw new NotFoundException("Party", id);
        if (p.Get<bool>("isStaff")) p["staff"] = await uow.Parties.StaffAsync(id, ct);
        return p;
    }

    public async Task<long> CreateAsync(UserContext u, PartyBody b, CancellationToken ct)
    {
        Authorise(u, b);
        var input = Validate(b);
        await using var uow = await uows.BeginAsync(u, ct);
        var code = StoreService.Blank(b.Code);
        if (code is null)
        {
            var prefix = b.IsVendor ? "V" : b.IsStaff && !b.IsCustomer ? "E" : "C";
            // The number series is per store (store scoping applies to it), so the store id is part of the code to keep codes unique company-wide.
            code = $"{prefix}{u.StoreId}-{await uow.Parties.NextCodeNumberAsync(u.CompanyId, u.StoreId, "PARTY-" + prefix, ct):D4}";
        }
        var id = await uow.Parties.InsertAsync(u.CompanyId, input, code, ct);
        if (b.IsStaff) await uow.Parties.UpsertStaffAsync(u.CompanyId, id, ToStaff(u, b.Staff), ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(UserContext u, long id, PartyBody b, CancellationToken ct)
    {
        Authorise(u, b);
        var input = Validate(b);
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Parties.GetAsync(id, ct) ?? throw new NotFoundException("Party", id);
        if (cur.Get<string>("code") == "WALKIN") throw new BusinessRuleException("The Cash Sale customer is built in and cannot be edited.");
        if (!await uow.Parties.UpdateAsync(id, input, b.RowVer, ct)) throw new ConflictException("This record was changed by someone else. Reload and try again.", "stale");
        if (b.IsStaff && b.Staff is not null) await uow.Parties.UpsertStaffAsync(u.CompanyId, id, ToStaff(u, b.Staff), ct);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Parties.GetAsync(id, ct) ?? throw new NotFoundException("Party", id);
        if (cur.Get<string>("code") == "WALKIN") throw new BusinessRuleException("The Cash Sale customer is built in and cannot be deleted.");
        if (cur.Get<long>("balance") != 0) throw new BusinessRuleException("A party with an outstanding balance cannot be deleted. Settle the balance first.", "has_balance");
        await uow.Parties.SoftDeleteAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task MoveStaffAsync(UserContext u, long id, MoveStaffBody b, CancellationToken ct)
    {
        u.RequireAny(P.UserAdmin, P.PayrollPrepare, P.MasterEdit);
        u.RequireStore(b.StoreId);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Parties.StaffAsync(id, ct) is null) throw new NotFoundException("Employee", id);
        await uow.Parties.MoveStaffAsync(u.CompanyId, id, b.StoreId, b.EffectiveOn ?? DateOnly.FromDateTime(DateTime.UtcNow), u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> StaffHistoryAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Parties.StaffHistoryAsync(id, ct);
    }

    private static void Authorise(UserContext u, PartyBody b)
    {
        if (b.IsVendor) u.Require(P.VendorManage);
        if (b.IsStaff) u.RequireAny(P.UserAdmin, P.PayrollPrepare, P.MasterEdit);
        if (b.IsCustomer && !b.IsVendor && !b.IsStaff) u.RequireAny(P.MasterEdit, P.SaleCreate);
    }

    private static PartyInput Validate(PartyBody b)
    {
        var c = new Check().NotBlank(b.Name, "name").MaxLength(b.Name, 150, "name")
            .Require(b.IsCustomer || b.IsVendor || b.IsStaff, "isCustomer", "Choose customer, vendor or staff.")
            .Require(b.Status is null or "ACTIVE" or "INACTIVE", "status", "Status must be ACTIVE or INACTIVE.")
            .Require(b.CreditLimit is null or >= 0, "creditLimit", "Credit limit cannot be negative.")
            .Require(b.Cnic is null || b.Cnic == "" || Regex.IsMatch(b.Cnic, @"^[0-9]{5}-[0-9]{7}-[0-9]$"), "cnic", "CNIC must look like 35202-1234567-1.")
            .Require(b.Email is null || b.Email == "" || Regex.IsMatch(b.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"), "email", "Email is not valid.")
            .Require(!b.IsStaff || b.Staff is not null, "staff", "Staff details are required for an employee.");
        c.ThrowIfAny();
        var b2 = StoreService.Blank;
        return new PartyInput(b.Name!.Trim(), b2(b.NameUr), b2(b.FatherName), b2(b.Cnic), b2(b.PhonePrimary), b2(b.PhoneSecondary), b2(b.Email), b2(b.Address),
            b.CityId, b.IsCustomer, b.IsVendor, b.IsStaff, b.IsVendor ? b.CreditLimit : null, b.Status ?? "ACTIVE");
    }

    private static StaffInput ToStaff(UserContext u, StaffBody? s)
    {
        if (s is null) throw new ValidationException("staff", "Staff details are required.");
        var store = s.StoreId ?? u.StoreId;
        u.RequireStore(store);
        if (store <= 0) throw new ValidationException("staff.storeId", "An employee works in a selling store.");
        if (s.BaseSalary < 0) throw new ValidationException("staff.baseSalary", "Salary cannot be negative.");
        if (s.PayFrequency is not (null or "MONTHLY" or "BIWEEKLY" or "WEEKLY")) throw new ValidationException("staff.payFrequency", "Pay frequency must be MONTHLY, BIWEEKLY or WEEKLY.");
        return new StaffInput(StoreService.Blank(s.Designation), s.IsManager, s.BaseSalary, s.PayFrequency ?? "MONTHLY", store, s.JoinedOn, s.LeftOn);
    }
}
