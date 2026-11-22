namespace EFootwear.DAL.Persistence;

/// <summary>
/// The PostgreSQL session settings that carry the caller's identity into the database. <see cref="DatabaseSession"/>
/// sets them with set_config(..., is_local := true) at the start of every transaction; row-level security and the
/// functions in db/schema_v7_uuid.sql read them. The names MUST match the SQL, so they live here and nowhere else.
///
///   CompanyId             -> core.current_company_id()      (uuid, empty when nobody is signed in)
///   UserId                -> core.current_user_id()         (uuid)
///   AllStores             -> core.store_allowed(store)      ('on' = every store of the company)
///   StoreIds              -> core.store_allowed(store)      (comma separated uuids of the assigned stores)
///   PlatformAdministrator -> platform.companies RLS policy  ('on' = platform administrator, may read every company)
/// </summary>
public static class SessionContextKeys
{
    public const string CompanyId = "app.company_id";
    public const string UserId = "app.user_id";
    public const string AllStores = "app.all_stores";
    public const string StoreIds = "app.store_ids";
    public const string PlatformAdministrator = "app.platform_admin";

    /// <summary>Value of the on/off settings when they are on.</summary>
    public const string On = "on";

    /// <summary>Value of the on/off settings when they are off.</summary>
    public const string Off = "off";
}
