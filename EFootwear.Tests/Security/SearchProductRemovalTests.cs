using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Security;

/// <summary>Migration 1502: holders of the removed Search Product permission get View Products instead.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class SearchProductRemovalTests(ApiTestEnvironment environment)
{
    private const string RemovedCode = "PRODUCTS.SEARCH_PRODUCT.VIEW";

    [Fact]
    public async Task Migration_1502_replaces_the_search_product_permission_with_view_products()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.PriceCheck.View);
        Assert.False(PermissionCatalog.IsKnownPermissionCode(RemovedCode));
        // the database as an earlier version left it
        await environment.Database.ExecuteAsOwnerAsync(
            $"insert into sec.permissions (code, description, module_code, screen_code, action_code, sort_order) values ('{RemovedCode}', 'Search Product: View', 'PRODUCTS', 'SEARCH_PRODUCT', 'VIEW', 0)");
        await environment.Database.ExecuteAsOwnerAsync(
            $"insert into sec.user_permissions (company_id, user_id, permission_code, granted_by, granted_at) select company_id, user_id, '{RemovedCode}', granted_by, granted_at from sec.user_permissions where company_id = '{scenario.CompanyId}' and permission_code = '{PermissionCodes.Products.PriceCheck.View}'");
        async Task<long> HoldersAsync(string permissionCode) => await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from sec.user_permissions where company_id = $1 and permission_code = $2", scenario.CompanyId, permissionCode);
        Assert.Equal(1L, await HoldersAsync(RemovedCode));
        Assert.Equal(0L, await HoldersAsync(PermissionCodes.Products.ViewProducts.View));

        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.MigrationsDirectory, "1502_remove_search_product_permission.sql"));
        await environment.Database.ExecuteAsOwnerAsync(script);

        Assert.Equal(1L, await HoldersAsync(PermissionCodes.Products.ViewProducts.View));
        Assert.Equal(0L, await HoldersAsync(RemovedCode));
        Assert.Equal(0L, await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from sec.permissions where code = $1", RemovedCode));
    }
}
