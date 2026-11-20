using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class UserRepository(Db db) : IUserRepository
{
    private const string Select = @"
        select u.id, u.username, u.full_name, u.role_code, u.status, u.all_stores, u.staff_party_id, u.must_change_password,
               u.last_login_at, u.locked_until, u.created_at, u.row_ver,
               (select coalesce(json_agg(us.store_id order by us.store_id), '[]'::json)::text from sec.user_stores us where us.company_id = u.company_id and us.user_id = u.id) as stores_json,
               (select coalesce(json_agg(p.permission_code order by p.permission_code), '[]'::json)::text from sec.user_permissions p where p.company_id = u.company_id and p.user_id = u.id) as permissions_json
          from sec.users u where u.company_id = @c and not u.is_deleted";

    public Task<Page<Row>> ListAsync(long companyId, PageQuery q, string? status, CancellationToken ct)
        => db.PageAsync(Select + " and (@st::text is null or u.status = @st) and (@q::text is null or u.username ilike @q or u.full_name ilike @q)",
            "username", new { c = companyId, st = status, q = string.IsNullOrWhiteSpace(q.Search) ? null : $"%{q.Search.Trim()}%" }, q, ct);

    public Task<Row?> GetAsync(long companyId, long id, CancellationToken ct)
        => db.RowAsync(Select + " and u.id = @id", new { c = companyId, id }, ct);

    public Task<long> InsertAsync(long companyId, string username, string hash, string fullName, string role, bool allStores, long? staffPartyId, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into sec.users (company_id, username, password_hash, full_name, role_code, all_stores, staff_party_id, must_change_password, created_by)
              values (@c, @u, @h, @f, @r, @a, @sp, true, core.current_user_id()) returning id",
            new { c = companyId, u = username, h = hash, f = fullName, r = role, a = allStores, sp = staffPartyId }, ct);

    public Task UpdateAsync(long companyId, long id, string fullName, string role, bool allStores, long? staffPartyId, CancellationToken ct)
        => db.ExecAsync("update sec.users set full_name = @f, role_code = @r, all_stores = @a, staff_party_id = @sp where company_id = @c and id = @id",
            new { c = companyId, id, f = fullName, r = role, a = allStores, sp = staffPartyId }, ct);

    public async Task ReplaceStoresAsync(long companyId, long id, IReadOnlyCollection<long> storeIds, CancellationToken ct)
    {
        await db.ExecAsync("delete from sec.user_stores where company_id = @c and user_id = @id", new { c = companyId, id }, ct);
        foreach (var s in storeIds)
            await db.ExecAsync("insert into sec.user_stores (company_id, user_id, store_id) values (@c, @id, @s)", new { c = companyId, id, s }, ct);
    }

    public async Task ReplacePermissionsAsync(long companyId, long id, IReadOnlyCollection<string> codes, long grantedBy, CancellationToken ct)
    {
        await db.ExecAsync("delete from sec.user_permissions where company_id = @c and user_id = @id", new { c = companyId, id }, ct);
        foreach (var p in codes)
            await db.ExecAsync("insert into sec.user_permissions (company_id, user_id, permission_code, granted_by) values (@c, @id, @p, @g)",
                new { c = companyId, id, p, g = grantedBy }, ct);
    }

    public Task<List<Row>> PermissionCatalogAsync(CancellationToken ct) => db.RowsAsync("select code, description from sec.permissions order by code", null, ct);
    public Task<List<Row>> RolesAsync(CancellationToken ct) => db.RowsAsync("select code, name from sec.roles where code <> 'PLATFORM_ADMIN' order by code", null, ct);

    public Task AdminResetPasswordAsync(long companyId, long target, string tempHash, CancellationToken ct)
        => db.ExecAsync("select sec.admin_reset_password(@c, @t, @h)", new { c = companyId, t = target, h = tempHash }, ct);

    public Task SetStatusAsync(long companyId, long target, string status, CancellationToken ct)
        => db.ExecAsync("select sec.set_user_status(@c, @t, @s)", new { c = companyId, t = target, s = status }, ct);

    public Task<List<Row>> LoginHistoryAsync(long companyId, long? userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        => db.RowsAsync(
            @"select out_attempt_id as id, out_user_id as user_id, out_username as username, out_outcome as outcome,
                     out_attempted_at as attempted_at, out_ip as ip, out_device as device, out_acted_by as acted_by
                from sec.login_history(@c, @u, @f, @t)",
            new { c = companyId, u = userId, f = from, t = to }, ct);

    public Task<long> ActiveOwnerCountAsync(long companyId, CancellationToken ct)
        => db.ScalarAsync<long>("select count(*) from sec.users where company_id = @c and role_code = 'COMPANY_OWNER' and status = 'ACTIVE' and not is_deleted", new { c = companyId }, ct);
}

public sealed class StoreRepository(Db db) : IStoreRepository
{
    private const string Select = "select s.id, s.code, s.name, s.is_head_office, s.city_id, ci.name as city, s.address, s.phone, s.is_active, s.row_ver " +
                                  "from core.stores s left join master.cities ci on ci.company_id = s.company_id and ci.id = s.city_id where s.company_id = @c";

    public Task<List<Row>> ListAsync(long companyId, bool includeInactive, CancellationToken ct)
        => db.RowsAsync(Select + (includeInactive ? "" : " and s.is_active") + " order by s.id", new { c = companyId }, ct);

    public Task<Row?> GetAsync(long companyId, long id, CancellationToken ct) => db.RowAsync(Select + " and s.id = @id", new { c = companyId, id }, ct);

    public Task<long> InsertAsync(long companyId, string code, string name, long? cityId, string? address, string? phone, CancellationToken ct)
        => db.ScalarAsync<long>("insert into core.stores (company_id, code, name, city_id, address, phone, created_by) values (@c, @code, @n, @city, @a, @p, core.current_user_id()) returning id",
            new { c = companyId, code, n = name, city = cityId, a = address, p = phone }, ct);

    public async Task<bool> UpdateAsync(long companyId, long id, string name, long? cityId, string? address, string? phone, bool isActive, long? rowVer, CancellationToken ct)
        => await db.ExecAsync("update core.stores set name = @n, city_id = @city, address = @a, phone = @p, is_active = @act where company_id = @c and id = @id and (@rv::bigint is null or row_ver = @rv)",
            new { c = companyId, id, n = name, city = cityId, a = address, p = phone, act = isActive, rv = rowVer }, ct) == 1;

    public Task<Row?> GetCompanyAsync(long companyId, CancellationToken ct)
        => db.RowAsync("select id, name, legal_name, ntn, strn, timezone, currency_code, rounding_mode, price_band_basis, plan_code, status, invoice_terms, row_ver from platform.companies where id = @c", new { c = companyId }, ct);

    public async Task<bool> UpdateCompanyAsync(long companyId, CompanyUpdate u, CancellationToken ct)
        => await db.ExecAsync(
            @"update platform.companies set name = @n, legal_name = @l, ntn = @ntn, strn = @strn, timezone = coalesce(@tz, timezone),
                     rounding_mode = coalesce(@rm, rounding_mode), price_band_basis = coalesce(@pb, price_band_basis), invoice_terms = @terms,
                     updated_by = core.current_user_id()
               where id = @c and (@rv::bigint is null or row_ver = @rv)",
            new { c = companyId, n = u.Name, l = u.LegalName, ntn = u.Ntn, strn = u.Strn, tz = u.Timezone, rm = u.RoundingMode, pb = u.PriceBandBasis, terms = u.InvoiceTerms, rv = u.RowVer }, ct) == 1;

    public Task<List<Row>> DevicesAsync(long companyId, CancellationToken ct)
        => db.RowsAsync("select id, store_id, device_uuid, label, is_active, last_seen_at from core.devices where company_id = @c order by store_id, id", new { c = companyId }, ct);
}

public sealed class MasterRepository(Db db) : IMasterRepository
{
    /// <summary>Slug → table. Only these names ever reach SQL text.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["departments"] = "departments", ["brands"] = "brands", ["seasons"] = "seasons", ["genders"] = "genders", ["colors"] = "colors",
        ["size-groups"] = "size_groups", ["product-types"] = "product_types", ["units"] = "units", ["locations"] = "locations", ["cities"] = "cities",
    };

    private static string T(string kind) => "master." + (Kinds.TryGetValue(kind, out var t) ? t : throw new ArgumentException("Unknown master list.", nameof(kind)));

    public Task<List<Row>> ListAsync(string kind, string? search, bool includeInactive, CancellationToken ct)
        => db.RowsAsync($"select id, code, name, name_ur, sort_order, is_active, row_ver from {T(kind)} where not is_deleted" +
                        (includeInactive ? "" : " and is_active") + " and (@q::text is null or name ilike @q or code ilike @q or name_ur ilike @q) order by sort_order, lower(name)",
            new { q = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%" }, ct);

    public Task<Row?> GetAsync(string kind, long id, CancellationToken ct)
        => db.RowAsync($"select id, code, name, name_ur, sort_order, is_active, row_ver from {T(kind)} where id = @id and not is_deleted", new { id }, ct);

    public Task<long> InsertAsync(string kind, long companyId, MasterInput m, CancellationToken ct)
        => db.ScalarAsync<long>($"insert into {T(kind)} (company_id, code, name, name_ur, sort_order, created_by) values (@c, @code, @n, @u, @s, core.current_user_id()) returning id",
            new { c = companyId, code = m.Code, n = m.Name, u = m.NameUr, s = m.SortOrder }, ct);

    public async Task<bool> UpdateAsync(string kind, long id, MasterInput m, bool isActive, long? rowVer, CancellationToken ct)
        => await db.ExecAsync($"update {T(kind)} set code = @code, name = @n, name_ur = @u, sort_order = @s, is_active = @a where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)",
            new { id, code = m.Code, n = m.Name, u = m.NameUr, s = m.SortOrder, a = isActive, rv = rowVer }, ct) == 1;

    public Task SoftDeleteAsync(string kind, long id, long userId, CancellationToken ct)
        => db.ExecAsync($"update {T(kind)} set is_deleted = true, deleted_at = now(), deleted_by = @u where id = @id and not is_deleted", new { id, u = userId }, ct);

    public Task<List<Row>> CategoriesAsync(string? search, long? departmentId, bool includeInactive, CancellationToken ct)
        => db.RowsAsync(
            @"select c.id, c.department_id, d.name as department, c.parent_id, p.name as parent, c.code, c.name, c.name_ur, c.sort_order, c.is_active, c.row_ver
                from master.categories c
                join master.departments d on d.company_id = c.company_id and d.id = c.department_id
                left join master.categories p on p.company_id = c.company_id and p.id = c.parent_id
               where not c.is_deleted" + (includeInactive ? "" : " and c.is_active") +
            @" and (@d::bigint is null or c.department_id = @d) and (@q::text is null or c.name ilike @q or c.name_ur ilike @q)
               order by d.name, c.sort_order, lower(c.name)",
            new { d = departmentId, q = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%" }, ct);

    public Task<long> InsertCategoryAsync(long companyId, long departmentId, long? parentId, MasterInput m, CancellationToken ct)
        => db.ScalarAsync<long>("insert into master.categories (company_id, department_id, parent_id, code, name, name_ur, sort_order, created_by) values (@c, @d, @p, @code, @n, @u, @s, core.current_user_id()) returning id",
            new { c = companyId, d = departmentId, p = parentId, code = m.Code, n = m.Name, u = m.NameUr, s = m.SortOrder }, ct);

    public async Task<bool> UpdateCategoryAsync(long id, long departmentId, long? parentId, MasterInput m, bool isActive, long? rowVer, CancellationToken ct)
        => await db.ExecAsync("update master.categories set department_id = @d, parent_id = @p, code = @code, name = @n, name_ur = @u, sort_order = @s, is_active = @a where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)",
            new { id, d = departmentId, p = parentId, code = m.Code, n = m.Name, u = m.NameUr, s = m.SortOrder, a = isActive, rv = rowVer }, ct) == 1;

    public Task SoftDeleteCategoryAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync("update master.categories set is_deleted = true, deleted_at = now(), deleted_by = @u where id = @id and not is_deleted", new { id, u = userId }, ct);

    public Task<List<Row>> SizesAsync(long? sizeGroupId, CancellationToken ct)
        => db.RowsAsync("select s.id, s.size_group_id, g.name as size_group, s.size_code, s.sort_order from master.sizes s join master.size_groups g on g.company_id = s.company_id and g.id = s.size_group_id where (@g::bigint is null or s.size_group_id = @g) order by g.name, s.sort_order, s.size_code",
            new { g = sizeGroupId }, ct);

    public Task<long> InsertSizeAsync(long companyId, long sizeGroupId, string sizeCode, int sortOrder, CancellationToken ct)
        => db.ScalarAsync<long>("insert into master.sizes (company_id, size_group_id, size_code, sort_order) values (@c, @g, @s, @o) returning id", new { c = companyId, g = sizeGroupId, s = sizeCode, o = sortOrder }, ct);

    public Task DeleteSizeAsync(long id, CancellationToken ct) => db.ExecAsync("delete from master.sizes where id = @id", new { id }, ct);
}

public sealed class PartyRepository(Db db) : IPartyRepository
{
    private const string Select = @"
        select p.id, p.code, p.name, p.name_ur, p.father_name, p.cnic, p.phone_primary, p.phone_secondary, p.email, p.address, p.city_id, ci.name as city,
               p.is_customer, p.is_vendor, p.is_staff, p.credit_limit, p.status, p.row_ver, coalesce(b.balance, 0) as balance
          from party.parties p
          left join party.party_balances b on b.company_id = p.company_id and b.party_id = p.id
          left join master.cities ci on ci.company_id = p.company_id and ci.id = p.city_id
         where not p.is_deleted";

    public Task<Page<Row>> ListAsync(PartyFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(Select +
            @" and (@cu::boolean is null or p.is_customer = @cu) and (@ve::boolean is null or p.is_vendor = @ve) and (@st2::boolean is null or p.is_staff = @st2)
               and (@status::text is null or p.status = @status) and (not @bal or coalesce(b.balance, 0) <> 0)
               and (@q::text is null or p.name ilike @q or p.code ilike @q or p.phone_primary ilike @q or p.cnic ilike @q or p.name_ur ilike @q)",
            "lower(p.name), p.id", new { cu = f.Customer, ve = f.Vendor, st2 = f.Staff, status = f.Status, bal = f.OnlyWithBalance, q = string.IsNullOrWhiteSpace(q.Search) ? null : $"%{q.Search.Trim()}%" }, q, ct);

    public Task<Row?> GetAsync(long id, CancellationToken ct) => db.RowAsync(Select + " and p.id = @id", new { id }, ct);

    public Task<long> InsertAsync(long companyId, PartyInput p, string code, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into party.parties (company_id, code, name, name_ur, father_name, cnic, phone_primary, phone_secondary, email, address, city_id,
                                         is_customer, is_vendor, is_staff, credit_limit, status, created_by)
              values (@c, @code, @n, @u, @fa, @cnic, @p1, @p2, @em, @ad, @ci, @cu, @ve, @st, @cl, @status, core.current_user_id()) returning id",
            Args(companyId, p, code), ct);

    public async Task<bool> UpdateAsync(long id, PartyInput p, long? rowVer, CancellationToken ct)
        => await db.ExecAsync(
            @"update party.parties set name = @n, name_ur = @u, father_name = @fa, cnic = @cnic, phone_primary = @p1, phone_secondary = @p2, email = @em, address = @ad,
                     city_id = @ci, is_customer = @cu, is_vendor = @ve, is_staff = @st, credit_limit = @cl, status = @status
               where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)",
            Args(0, p, "", id, rowVer), ct) == 1;

    public Task SoftDeleteAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync("update party.parties set is_deleted = true, deleted_at = now(), deleted_by = @u where id = @id and not is_deleted", new { id, u = userId }, ct);

    public Task<long> NextCodeNumberAsync(long companyId, long storeId, string series, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series)", new { c = companyId, s = storeId, series }, ct);

    public Task UpsertStaffAsync(long companyId, long partyId, StaffInput s, CancellationToken ct)
        => db.ExecAsync(
            @"insert into party.staff_profiles (company_id, party_id, designation, is_manager, base_salary, pay_frequency, current_store_id, joined_on, left_on, created_by)
              values (@c, @p, @d, @m, @b, @f, @s, @j, @l, core.current_user_id())
              on conflict (company_id, party_id) do update set designation = excluded.designation, is_manager = excluded.is_manager, base_salary = excluded.base_salary,
                     pay_frequency = excluded.pay_frequency, joined_on = excluded.joined_on, left_on = excluded.left_on",
            new { c = companyId, p = partyId, d = s.Designation, m = s.IsManager, b = s.BaseSalary, f = s.PayFrequency, s = s.CurrentStoreId, j = s.JoinedOn, l = s.LeftOn }, ct);

    public Task<Row?> StaffAsync(long partyId, CancellationToken ct)
        => db.RowAsync("select sp.party_id, sp.designation, sp.is_manager, sp.base_salary, sp.pay_frequency, sp.current_store_id, st.name as current_store, sp.joined_on, sp.left_on " +
                       "from party.staff_profiles sp join core.stores st on st.company_id = sp.company_id and st.id = sp.current_store_id where sp.party_id = @p", new { p = partyId }, ct);

    public async Task MoveStaffAsync(long companyId, long partyId, long toStore, DateOnly effectiveOn, long userId, CancellationToken ct)
    {
        var from = await db.ScalarAsync<long?>("select current_store_id from party.staff_profiles where company_id = @c and party_id = @p for update", new { c = companyId, p = partyId }, ct);
        if (from == toStore) return;
        await db.ExecAsync("update party.staff_profiles set current_store_id = @t where company_id = @c and party_id = @p", new { c = companyId, p = partyId, t = toStore }, ct);
        await db.ExecAsync("insert into party.staff_store_history (company_id, party_id, from_store_id, to_store_id, effective_on, changed_by) values (@c, @p, @f, @t, @e, @u)",
            new { c = companyId, p = partyId, f = from, t = toStore, e = effectiveOn, u = userId }, ct);
    }

    public Task<List<Row>> StaffHistoryAsync(long partyId, CancellationToken ct)
        => db.RowsAsync("select h.id, h.from_store_id, f.name as from_store, h.to_store_id, t.name as to_store, h.effective_on, h.changed_at from party.staff_store_history h " +
                        "left join core.stores f on f.company_id = h.company_id and f.id = h.from_store_id join core.stores t on t.company_id = h.company_id and t.id = h.to_store_id " +
                        "where h.party_id = @p order by h.effective_on desc, h.id desc", new { p = partyId }, ct);

    private static object Args(long companyId, PartyInput p, string code, long id = 0, long? rv = null) => new
    {
        c = companyId, code, id, rv, n = p.Name, u = p.NameUr, fa = p.FatherName, cnic = p.Cnic, p1 = p.PhonePrimary, p2 = p.PhoneSecondary, em = p.Email, ad = p.Address,
        ci = p.CityId, cu = p.IsCustomer, ve = p.IsVendor, st = p.IsStaff, cl = p.CreditLimit, status = p.Status,
    };
}
