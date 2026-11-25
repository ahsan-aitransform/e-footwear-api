using System.Text;
using EFootwear.DAL.MasterData.Configurations;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.CashManagement;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Customers;
using EFootwear.Domain.Staff;
using EFootwear.Domain.Vendors;
using EFootwear.Domain.Core;
using EFootwear.Domain.Platform;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// EF Core model of the tables the API reads and writes directly. The database stays the authority:
/// row-level security, triggers and the sec.* / platform.* functions are untouched. EF only maps entity classes
/// (EFootwear.Domain) to existing tables; it never creates or changes the schema (db/migrations does that).
/// Columns the entity does not list (audit columns) keep their database defaults.
///
/// Mapping lives in one <c>IEntityTypeConfiguration&lt;T&gt;</c> class per entity, in the module folder
/// (DAL/&lt;Module&gt;/Configurations). They are found automatically; adding a module never edits this file except to add
/// its <c>DbSet</c>. Column names default to snake_case (CompanyId becomes company_id); a configuration may override one
/// with HasColumnName.
/// </summary>
public sealed class ApplicationDatabaseContext(DbContextOptions<ApplicationDatabaseContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
    public DbSet<UserStoreAccess> UserStoreAccesses => Set<UserStoreAccess>();
    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<RoleTemplatePermission> RoleTemplatePermissions => Set<RoleTemplatePermission>();
    public DbSet<PosDevice> PosDevices => Set<PosDevice>();
    public DbSet<UserQuickAction> UserQuickActions => Set<UserQuickAction>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserActivityFeedEntry> UserActivityFeedEntries => Set<UserActivityFeedEntry>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SizeCode> SizeCodes => Set<SizeCode>();
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();
    public DbSet<GeneralLedgerAccount> GeneralLedgerAccounts => Set<GeneralLedgerAccount>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorBalance> VendorBalances => Set<VendorBalance>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffStoreHistory> StaffStoreHistories => Set<StaffStoreHistory>();
    public DbSet<StaffBalance> StaffBalances => Set<StaffBalance>();
    public DbSet<CashPromise> CashPromises => Set<CashPromise>();
    public DbSet<CashCount> CashCounts => Set<CashCount>();
    public DbSet<CashCountDenomination> CashCountDenominations => Set<CashCountDenomination>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<VariantPrice> VariantPrices => Set<VariantPrice>();
    public DbSet<StorePriceOverride> StorePriceOverrides => Set<StorePriceOverride>();
    public DbSet<PriceEvent> PriceEvents => Set<PriceEvent>();
    public DbSet<PriceBand> PriceBands => Set<PriceBand>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<BarcodeHistoryEntry> BarcodeHistoryEntries => Set<BarcodeHistoryEntry>();
    // <database-sets:end> (add one DbSet per new entity above this line)

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDatabaseContext).Assembly);

        // The ten simple master lists share one class (MasterListItem) but live in ten tables: shared-type entities, which
        // IEntityTypeConfiguration cannot express (module master-data).
        MasterListMappings.Apply(modelBuilder);

        // Column names in the database are snake_case; the classes use PascalCase. A name set explicitly in a
        // configuration is kept.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                    property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string propertyName)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < propertyName.Length; index++)
        {
            if (char.IsUpper(propertyName[index]) && index > 0) builder.Append('_');
            builder.Append(char.ToLowerInvariant(propertyName[index]));
        }
        return builder.ToString();
    }
}
