using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Services;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Services;
using EFootwear.DAL.Common;
using EFootwear.DAL.Core.Repositories;
using EFootwear.DAL.Core.Storage;
using EFootwear.DAL.Persistence;
using EFootwear.DAL.Platform.Repositories;
using EFootwear.DAL.Security;
using EFootwear.DAL.Security.Permissions;
using EFootwear.DAL.Security.Repositories;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.DAL.Catalogue.Repositories;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Services;
using EFootwear.BLL.CashManagement.Abstractions;
using EFootwear.BLL.CashManagement.Services;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Services;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Contacts.Services;
using EFootwear.BLL.Customers.Abstractions;
using EFootwear.BLL.Customers.Services;
using EFootwear.BLL.Staff.Abstractions;
using EFootwear.BLL.Staff.Services;
using EFootwear.BLL.Vendors.Abstractions;
using EFootwear.BLL.Vendors.Services;
using EFootwear.DAL.Accounting.Repositories;
using EFootwear.DAL.CashManagement.Repositories;
using EFootwear.DAL.MasterData.Repositories;
using EFootwear.DAL.Contacts.Repositories;
using EFootwear.DAL.Customers.Repositories;
using EFootwear.DAL.Staff.Repositories;
using EFootwear.DAL.Vendors.Repositories;
using EFootwear.BLL.HumanResources.Abstractions;
using EFootwear.BLL.HumanResources.Services;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Services;
using EFootwear.BLL.Payroll.Abstractions;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Services;
using EFootwear.BLL.Payroll.Services;
using EFootwear.DAL.HumanResources.Repositories;
using EFootwear.DAL.Commissions.Repositories;
using EFootwear.DAL.Payroll.Repositories;
using EFootwear.DAL.Promotions.Repositories;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Services;
using EFootwear.DAL.Inventory.Repositories;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Services;
using EFootwear.DAL.Sales.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace EFootwear.DAL;

public static class DependencyInjection
{
    public const string DefaultConnectionStringName = "Default";

    /// <summary>
    /// Registers everything the Api needs. This is the one place that connects interfaces
    /// (defined in the BLL) to their real implementations (in the DAL).
    ///
    /// HOW TO ADD A MODULE (details: ARCHITECTURE.md): add one line per service and repository inside the block of your
    /// module, directly above its end marker, using the Edit tool (never rewrite this file). One block per module of
    /// Part 25.3; the markers are unique, so several people can add lines at the same time.
    /// </summary>
    public static IServiceCollection AddDal(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DefaultConnectionStringName)
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set (use dotnet user-secrets).");

        // One shared connection pool for the whole app.
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);

        // EF Core, used by the platform-administrator repository (own short-lived contexts). Request-scoped work uses the
        // context that DatabaseSession builds on its own connection and transaction.
        services.AddSingleton<SlowQueryInterceptor>();
        services.AddDbContextFactory<ApplicationDatabaseContext>((serviceProvider, options) =>
            options.UseNpgsql(dataSource).AddInterceptors(serviceProvider.GetRequiredService<SlowQueryInterceptor>()));

        // At startup: refuse to run as a superuser (it would bypass row-level security), then publish the permission catalog.
        services.AddHostedService<DatabaseRoleGuard>();
        services.AddHostedService<PermissionCatalogSynchronizer>();

        // Scoped = one instance per HTTP request. Authentication and business code share the
        // SAME RequestContext and DatabaseSession, so they see the same user and the same transaction.
        services.AddScoped<RequestContext>();
        services.AddScoped<ICurrentUser>(serviceProvider => serviceProvider.GetRequiredService<RequestContext>());
        services.AddScoped<DatabaseSession>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<DatabaseSession>());
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

        // ===================== Module registrations =====================

        // --- Platform: companies, platform administrators ---
        services.AddScoped<IPlatformAdministratorRepository, PlatformAdministratorRepository>();
        services.AddScoped<PlatformAdministratorAuthenticationService>();
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<PlatformService>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<PlanAdministrationService>();
        services.AddScoped<ICompanyAdministrationRepository, CompanyAdministrationRepository>();
        services.AddScoped<CompanyAdministrationService>();
        services.AddScoped<IAdministratorAuditRepository, AdministratorAuditRepository>();
        services.AddScoped<AdministratorAuditService>();
        services.AddScoped<IBackupStatusRepository, BackupStatusRepository>();
        services.AddScoped<BackupStatusService>();
        // <module-registrations:platform:end>

        // --- Core: stores, devices, numbering ---
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<StoreService>();
        services.AddScoped<StoreAccessGuard>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ICompanyProfileRepository, CompanyProfileRepository>();
        services.AddScoped<CompanySettingsService>();
        services.AddScoped<CompanyProfileService>();
        services.AddScoped<IStoreManagementRepository, StoreManagementRepository>();
        services.AddScoped<StoreManagementService>();
        services.AddScoped<IDocumentNumberingRepository, DocumentNumberingRepository>();
        services.AddScoped<DocumentNumberingService>();
        services.AddScoped<IPosTerminalRepository, PosTerminalRepository>();
        services.AddScoped<PosTerminalService>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IFileStorage>(serviceProvider =>
            new LocalFolderFileStorage(ResolveFileStorageRootDirectory(configuration, serviceProvider.GetRequiredService<IHostEnvironment>())));
        services.AddScoped<CompanyLogoService>();
        // <module-registrations:core:end>

        // --- Security: sign-in, users, roles, permissions ---
        services.AddScoped<IUserAuthenticationRepository, UserAuthenticationRepository>();
        services.AddScoped<UserAuthenticationService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<CurrentSessionService>();
        services.AddSingleton<PermissionCatalogService>();
        services.AddScoped<IUserPermissionRepository, UserPermissionRepository>();
        services.AddScoped<IRoleTemplateRepository, RoleTemplateRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IUserActivityRepository, UserActivityRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IUserQuickActionRepository, UserQuickActionRepository>();
        services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
        services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
        services.AddSingleton<IDeviceTokenService, DeviceTokenService>();
        services.AddSingleton<IPasswordResetTokenDelivery, NoDeliveryChannelConfiguredDelivery>();
        services.AddScoped<UserAccountSecurityService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<UserPermissionService>();
        services.AddScoped<RoleTemplateService>();
        services.AddScoped<UserSessionService>();
        services.AddScoped<UserActivityService>();
        services.AddScoped<UserQuickActionService>();
        services.AddScoped<DeviceManagementService>();
        // <module-registrations:security:end>

        // --- MasterData ---
        services.AddScoped<IRowUsageRepository, RowUsageRepository>();
        services.AddScoped<IMasterListRepository, MasterListRepository>();
        services.AddScoped<MasterListService>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<CategoryService>();
        services.AddScoped<ISizeRepository, SizeRepository>();
        services.AddScoped<SizeService>();
        // <module-registrations:master-data:end>

        // --- Contacts: customers, vendors and staff ---
        services.AddScoped<IContactCodeGenerator, ContactCodeGenerator>();
        services.AddScoped<IContactLedgerRepository, ContactLedgerRepository>();
        services.AddScoped<ContactFieldValidator>();
        services.AddScoped<ContactOpeningBalanceApplier>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerLedgerService>();
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddScoped<VendorService>();
        services.AddScoped<VendorLedgerService>();
        services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        services.AddScoped<StaffMemberService>();
        services.AddScoped<StaffTransferService>();
        services.AddScoped<StaffLedgerService>();
        // <module-registrations:contacts:end>

        // --- Catalogue ---
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPriceHistoryRepository, PriceHistoryRepository>();
        services.AddScoped<IPricingEngineRepository, PricingEngineRepository>();
        services.AddScoped<IProductListingRepository, ProductListingRepository>();
        services.AddScoped<IProductLedgerRepository, ProductLedgerRepository>();
        services.AddScoped<IBulkPriceRepository, BulkPriceRepository>();
        services.AddScoped<IProductDiscountRepository, ProductDiscountRepository>();
        services.AddScoped<IProductAttributeRepository, ProductAttributeRepository>();
        services.AddScoped<IBarcodeRepository, BarcodeRepository>();
        services.AddScoped<IPriceBandRepository, PriceBandRepository>();
        services.AddScoped<IProductImageRepository, ProductImageRepository>();
        services.AddScoped<ProductCatalogService>();
        services.AddScoped<ProductListingService>();
        services.AddScoped<ProductLedgerService>();
        services.AddScoped<BulkPriceChangeService>();
        services.AddScoped<ProductDiscountService>();
        services.AddScoped<ProductAttributeService>();
        services.AddScoped<BarcodeService>();
        services.AddScoped<PriceBandService>();
        services.AddScoped<ProductImageService>();
        // <module-registrations:catalogue:end>

        // --- Inventory ---
        services.AddScoped<IInventoryPostingRepository, InventoryPostingRepository>();
        services.AddScoped<IInventoryReadRepository, InventoryReadRepository>();
        services.AddScoped<InventoryLookupService>();
        services.AddScoped<StockAdjustmentService>();
        services.AddScoped<StockCountFileService>();
        services.AddScoped<StockAuditService>();
        services.AddScoped<ProductClaimService>();
        services.AddScoped<StockTransferService>();
        services.AddScoped<InventoryReportService>();
        // <module-registrations:inventory:end>

        // --- Sales ---
        services.AddScoped<ISaleInvoicePostingRepository, SaleInvoicePostingRepository>();
        services.AddScoped<ISaleInvoiceReadRepository, SaleInvoiceReadRepository>();
        services.AddScoped<SalePrintContextService>();
        services.AddScoped<SaleEntryService>();
        services.AddScoped<SaleInvoiceQueryService>();
        services.AddScoped<SaleInvoicePostingService>();
        // <module-registrations:sales:end>

        // --- Purchases ---
        // <module-registrations:purchases:end>

        // --- Orders ---
        // <module-registrations:orders:end>

        // --- Accounting ---
        services.AddScoped<IChartOfAccountsRepository, ChartOfAccountsRepository>();
        services.AddScoped<ChartOfAccountsService>();
        services.AddScoped<IOpeningBalanceRepository, OpeningBalanceRepository>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<IVoucherRepository, VoucherRepository>();
        services.AddScoped<VoucherService>();
        services.AddScoped<IAccountReportRepository, AccountReportRepository>();
        services.AddScoped<AccountReportService>();
        services.AddScoped<IAccountingPeriodRepository, AccountingPeriodRepository>();
        services.AddScoped<AccountingPeriodService>();
        services.AddScoped<ICashAndBankBalanceRepository, CashAndBankBalanceRepository>();
        services.AddScoped<CashAndBankBalanceService>();
        // <module-registrations:accounting:end>

        // --- CashManagement ---
        services.AddScoped<IPromiseRepository, PromiseRepository>();
        services.AddScoped<PromiseService>();
        services.AddScoped<ICashCountRepository, CashCountRepository>();
        services.AddScoped<CashCountService>();
        // <module-registrations:cash-management:end>

        // --- HumanResources ---
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<StaffDirectoryService>();
        services.AddScoped<StaffSalaryService>();
        // <module-registrations:human-resources:end>

        // --- Payroll ---
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddScoped<PayrollSheetService>();
        services.AddScoped<IPayrollReportRepository, PayrollReportRepository>();
        services.AddScoped<PayrollReportService>();
        // <module-registrations:payroll:end>

        // --- Commissions ---
        services.AddScoped<ICommissionPlanRepository, CommissionPlanRepository>();
        services.AddScoped<CommissionPlanService>();
        services.AddScoped<ICommissionEntryRepository, CommissionEntryRepository>();
        services.AddScoped<CommissionEntryService>();
        services.AddScoped<ICommissionReportRepository, CommissionReportRepository>();
        services.AddScoped<CommissionReportService>();
        // <module-registrations:commissions:end>

        // --- Expenses ---
        // <module-registrations:expenses:end>

        // --- Promotions ---
        services.AddScoped<IPromotionRuleRepository, PromotionRuleRepository>();
        services.AddScoped<PromotionRuleService>();
        services.AddScoped<SpecialOfferLineService>();
        services.AddScoped<IPromotionDealRepository, PromotionDealRepository>();
        services.AddScoped<PromotionDealService>();
        services.AddScoped<IPromotionConflictRepository, PromotionConflictRepository>();
        services.AddScoped<PromotionConflictService>();
        // <module-registrations:promotions:end>

        // --- Integrations ---
        // <module-registrations:integrations:end>

        // --- OfflineSynchronization ---
        // <module-registrations:offline-synchronization:end>

        // --- Reporting ---
        // <module-registrations:reporting:end>

        // --- Audit ---
        // <module-registrations:audit:end>

        return services;
    }

    public const string FileStorageRootDirectoryConfigurationKey = "FileStorage:RootDirectory";
    private const string DefaultFileStorageFolderName = "storage";

    /// <summary>Where uploaded files are kept: FileStorage:RootDirectory, or a "storage" folder inside the content root.</summary>
    private static string ResolveFileStorageRootDirectory(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var configured = configuration[FileStorageRootDirectoryConfigurationKey];
        var root = string.IsNullOrWhiteSpace(configured) ? DefaultFileStorageFolderName : configured;
        return Path.IsPathRooted(root) ? root : Path.Combine(hostEnvironment.ContentRootPath, root);
    }
}
