using EFootwear.Application.Admin;
using EFootwear.Application.Auth;
using EFootwear.Application.Catalog;
using EFootwear.Application.Finance;
using EFootwear.Application.Inventory;
using EFootwear.Application.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace EFootwear.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<AuthService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<StoreService>();
        services.AddSingleton<MasterService>();
        services.AddSingleton<PartyService>();
        services.AddSingleton<PriceRefresher>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<StockService>();
        services.AddSingleton<PromoService>();
        services.AddSingleton<SalesService>();
        services.AddSingleton<PurchaseService>();
        services.AddSingleton<TransferService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<AccountingService>();
        services.AddSingleton<ExpenseService>();
        services.AddSingleton<HrService>();
        services.AddSingleton<CashService>();
        services.AddSingleton<ReportService>();
        return services;
    }
}
