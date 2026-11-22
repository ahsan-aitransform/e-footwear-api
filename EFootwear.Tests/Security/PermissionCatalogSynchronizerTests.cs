using EFootwear.DAL.Security.Permissions;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using Npgsql;

namespace EFootwear.Tests.Security;

[Collection(ApiIntegrationCollection.Name)]
public class PermissionCatalogSynchronizerTests(ApiTestEnvironment environment)
{
    private const int LegacyPermissionCount = 38;

    [Fact]
    public async Task Startup_publishes_the_whole_catalog_and_running_it_again_changes_nothing()
    {
        _ = environment.ApiFactory.Server;   // starting the API runs the synchronizer once
        await using var dataSource = NpgsqlDataSource.Create(environment.Database.ApplicationLoginConnectionString);

        var changedRowCount = await PermissionCatalogSynchronizer.SynchronizeAsync(dataSource, CancellationToken.None);

        Assert.Equal(0, changedRowCount);
        Assert.Equal((long)PermissionCatalog.AllPermissionCodes.Count,
            await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from sec.permissions where module_code is not null"));
        Assert.Equal((long)LegacyPermissionCount,
            await environment.Database.QueryScalarAsOwnerAsync<long>(
                "select count(*) from sec.permissions where module_code = $1", PermissionCatalog.RestrictedOperationsModuleCode));
    }

    [Fact]
    public async Task A_changed_row_is_repaired_and_nothing_else_is_touched()
    {
        _ = environment.ApiFactory.Server;
        await using var dataSource = NpgsqlDataSource.Create(environment.Database.ApplicationLoginConnectionString);
        await environment.Database.ExecuteAsOwnerAsync(
            "update sec.permissions set sort_order = -1 where code = $1", PermissionCodes.Products.NewProduct.Add);

        var changedRowCount = await PermissionCatalogSynchronizer.SynchronizeAsync(dataSource, CancellationToken.None);

        Assert.Equal(1, changedRowCount);
        var expected = PermissionCatalog.AllEntries.Single(entry => entry.Action.PermissionCode == PermissionCodes.Products.NewProduct.Add).SortOrder;
        Assert.Equal(expected, await environment.Database.QueryScalarAsOwnerAsync<int>(
            "select sort_order from sec.permissions where code = $1", PermissionCodes.Products.NewProduct.Add));
    }

    [Fact]
    public async Task Legacy_codes_keep_the_exact_description_the_schema_gave_them()
    {
        _ = environment.ApiFactory.Server;

        Assert.Equal("Create sales", await environment.Database.QueryScalarAsOwnerAsync<string>(
            "select description from sec.permissions where code = 'SALE_CREATE'"));
        Assert.Equal("Decide offline sync conflicts (accept by hand or reject, with a note)", await environment.Database.QueryScalarAsOwnerAsync<string>(
            "select description from sec.permissions where code = 'SYNC_CONFLICT_RESOLVE'"));
    }
}
