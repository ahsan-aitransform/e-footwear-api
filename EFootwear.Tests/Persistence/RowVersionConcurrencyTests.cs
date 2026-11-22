using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Persistence;

/// <summary>row_ver as an optimistic-concurrency token: a stale copy cannot silently overwrite a newer change.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class RowVersionConcurrencyTests(ApiTestEnvironment environment)
{
    private ApplicationDatabaseContext CreateOwnerContext() => new(
        new DbContextOptionsBuilder<ApplicationDatabaseContext>().UseNpgsql(environment.Database.OwnerConnectionString).Options);

    [Fact]
    public async Task Saving_a_stale_copy_of_a_record_fails_instead_of_overwriting()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);

        await using var firstContext = CreateOwnerContext();
        await using var secondContext = CreateOwnerContext();
        var firstCopy = await firstContext.Stores.SingleAsync(store => store.CompanyId == company.CompanyId && store.Id == company.FirstStoreId);
        var secondCopy = await secondContext.Stores.SingleAsync(store => store.CompanyId == company.CompanyId && store.Id == company.FirstStoreId);
        var originalRowVersion = firstCopy.RowVersion;

        firstCopy.Name = "Changed by the first person";
        await firstContext.SaveChangesAsync();
        Assert.True(firstCopy.RowVersion > originalRowVersion, "the trigger's new row_ver is read back");

        secondCopy.Name = "Changed by the second person";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        Assert.Equal("Changed by the first person", await environment.Database.QueryScalarAsOwnerAsync<string>(
            "select name from core.stores where company_id = $1 and id = $2", company.CompanyId, company.FirstStoreId));
    }
}
