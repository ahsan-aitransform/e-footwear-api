using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Domain.Core;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>Document numbering: the continuation of every series of a store, moved forward only.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class DocumentNumberingApiTests(ApiTestEnvironment environment)
{
    private static long NextValueOf(JsonElement list, string seriesCode) =>
        list.EnumerateArray().Single(series => series.GetProperty("seriesCode").GetString() == seriesCode).GetProperty("nextValue").GetInt64();

    [Fact]
    public async Task Every_known_series_is_listed_and_can_be_moved_forward_never_back()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var storeId = scenario.Company.FirstStoreId;

        var list = await (await scenario.OwnerClient.GetAsync(SettingsUrls.DocumentNumbering(storeId))).ReadSuccessJsonAsync();
        Assert.Equal(DocumentNumberSeriesCodes.All.Count, list.GetArrayLength());
        Assert.All(list.EnumerateArray(), series => Assert.False(series.GetProperty("isStored").GetBoolean()));
        Assert.Equal(1, NextValueOf(list, DocumentNumberSeriesCodes.SaleInvoice));

        async Task<HttpResponseMessage> SetAsync(long nextValue) => await scenario.OwnerClient.PutAsJsonAsync(
            SettingsUrls.DocumentNumbering(), new { storeId, seriesCode = DocumentNumberSeriesCodes.SaleInvoice, nextValue });

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(500)).StatusCode);
        var afterSet = await (await scenario.OwnerClient.GetAsync(SettingsUrls.DocumentNumbering(storeId))).ReadSuccessJsonAsync();
        Assert.Equal(500, NextValueOf(afterSet, DocumentNumberSeriesCodes.SaleInvoice));

        await (await SetAsync(499)).AssertProblemAsync(HttpStatusCode.BadRequest, "DOCUMENT_NUMBER_WOULD_REPEAT");
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(500)).StatusCode);

        // two numbers are issued by the database function the modules use; the series now continues at 502
        await environment.Database.ExecuteAsOwnerAsync("select core.next_number($1, $2, $3)", scenario.Company.CompanyId, storeId, DocumentNumberSeriesCodes.SaleInvoice);
        await environment.Database.ExecuteAsOwnerAsync("select core.next_number($1, $2, $3)", scenario.Company.CompanyId, storeId, DocumentNumberSeriesCodes.SaleInvoice);
        await (await SetAsync(501)).AssertProblemAsync(HttpStatusCode.BadRequest, "DOCUMENT_NUMBER_WOULD_REPEAT");
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(900)).StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_are_refused()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var storeId = scenario.Company.FirstStoreId;

        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.DocumentNumbering(), new { storeId, seriesCode = "bad code", nextValue = 5 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.DocumentNumbering(), new { storeId, seriesCode = "SALE_INVOICE", nextValue = 0 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.GetAsync(SettingsUrls.DocumentNumbering()))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Numbering_needs_its_permission_and_never_reaches_another_company_or_store()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        var secondStoreId = await first.CreateStoreAsync("S2");
        var viewer = await first.CreateUserClientAsync("STORE_MANAGER", [secondStoreId], PermissionCodes.Settings.DocumentNumbering.View);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(SettingsUrls.DocumentNumbering(secondStoreId))).StatusCode);
        await (await viewer.GetAsync(SettingsUrls.DocumentNumbering(first.Company.FirstStoreId))).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await viewer.PutAsJsonAsync(SettingsUrls.DocumentNumbering(), new { storeId = secondStoreId, seriesCode = "SALE_INVOICE", nextValue = 10 }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        await (await second.OwnerClient.GetAsync(SettingsUrls.DocumentNumbering(first.Company.FirstStoreId))).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await second.OwnerClient.PutAsJsonAsync(SettingsUrls.DocumentNumbering(),
            new { storeId = first.Company.FirstStoreId, seriesCode = "SALE_INVOICE", nextValue = 10 })).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
