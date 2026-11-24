using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;
using static EFootwear.Tests.Inventory.InventoryTestHelpers;

namespace EFootwear.Tests.Inventory;

/// <summary>Product Claim and Claim Approval (docx 4.16, 4.17, 8.31): a claim is a request, stock moves only when it is approved, several claims are approved all or nothing.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductClaimApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, InventoryItem Item)> NewShopAsync(decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await LookupsAsync(scenario);
        return (scenario, await CreateItemAsync(environment, scenario, lookups, "Gents Sandal", stock));
    }

    private static string ApproveUrl => $"{ClaimApprovalsUrl}/approve";

    [Fact]
    public async Task A_claim_is_a_request_that_leaves_the_stock_alone_until_it_is_approved()
    {
        var (scenario, item) = await NewShopAsync();

        var claim = await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 2);
        Assert.Equal("REQUESTED", claim.GetProperty("statusCode").GetString());
        Assert.Equal(1, claim.GetProperty("claimNumber").GetInt64());
        Assert.Equal(item.Barcode, claim.GetProperty("barcode").GetString());
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        var approved = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, ApproveUrl, new { claimIds = new[] { claim.GetProperty("id").GetGuid() } });

        Assert.Equal(1, approved.GetProperty("approvedCount").GetInt32());
        Assert.Equal(8, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        var list = await GetJsonAsync(scenario.OwnerClient, $"{ClaimsUrl}?statusCode=APPROVED");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var again = await scenario.OwnerClient.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claim.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(8, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task Requesting_a_claim_twice_with_the_same_transaction_id_gives_the_same_claim()
    {
        var (scenario, item) = await NewShopAsync();
        var transactionId = Guid.NewGuid();

        var first = await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1, transactionId);
        var second = await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1, transactionId);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, (await GetJsonAsync(scenario.OwnerClient, ClaimsUrl)).GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_rejected_claim_needs_a_reason_keeps_the_stock_and_cannot_be_approved()
    {
        var (scenario, item) = await NewShopAsync();
        var claimId = (await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync($"{ClaimApprovalsUrl}/{claimId}/reject", new { reason = " " })).StatusCode);
        var rejected = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{ClaimApprovalsUrl}/{claimId}/reject", new { reason = "Normal wear" });

        Assert.Equal("REJECTED", rejected.GetProperty("statusCode").GetString());
        Assert.Equal("Normal wear", rejected.GetProperty("rejectReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claimId } })).StatusCode);
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task Approving_several_claims_is_all_or_nothing_when_one_of_them_cannot_be_taken_out_of_stock()
    {
        var (scenario, item) = await NewShopAsync(stock: 5);
        var lookups = await LookupsAsync(scenario);
        var scarce = await CreateItemAsync(environment, scenario, lookups, "Kids Shoe", 1);
        var fine = (await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 2)).GetProperty("id").GetGuid();
        var tooMany = (await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, scarce.VariantId, 3)).GetProperty("id").GetGuid();

        var response = await scenario.OwnerClient.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { fine, tooMany } });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(5, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(1, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, scarce.VariantId));
        var requested = await GetJsonAsync(scenario.OwnerClient, $"{ClaimsUrl}?statusCode=REQUESTED");
        Assert.Equal(2, requested.GetProperty("totalCount").GetInt64());

        var onlyFine = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, ApproveUrl, new { claimIds = new[] { fine } });
        Assert.Equal(1, onlyFine.GetProperty("approvedCount").GetInt32());
        Assert.Equal(3, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Theory]
    [InlineData("zero quantity")]
    [InlineData("unknown reason")]
    [InlineData("nothing selected")]
    public async Task Claims_and_approvals_with_wrong_input_are_refused(string problem)
    {
        var (scenario, item) = await NewShopAsync();
        var response = problem switch
        {
            "zero quantity" => await scenario.OwnerClient.PostAsJsonAsync(ClaimsUrl, new { storeId = scenario.FirstStoreId, claimDate = TestDate, clientTransactionId = Guid.NewGuid(), variantId = item.VariantId, quantity = 0, reasonCode = "DAMAGED", note = (string?)null }),
            "unknown reason" => await scenario.OwnerClient.PostAsJsonAsync(ClaimsUrl, new { storeId = scenario.FirstStoreId, claimDate = TestDate, clientTransactionId = Guid.NewGuid(), variantId = item.VariantId, quantity = 1, reasonCode = "BORED", note = (string?)null }),
            _ => await scenario.OwnerClient.PostAsJsonAsync(ApproveUrl, new { claimIds = Array.Empty<Guid>() }),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_permission_matrix_a_requester_cannot_approve_and_other_stores_and_companies_cannot_see_the_claim()
    {
        var (scenario, item) = await NewShopAsync();
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var claim = await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1);
        var claimId = claim.GetProperty("id").GetGuid();

        var requester = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Products.ProductClaim.View, PermissionCodes.Products.ProductClaim.Add, PermissionCodes.RestrictedOperations.Stock.ClaimRequest,
            PermissionCodes.Products.ClaimApproval.View);
        Assert.Equal(HttpStatusCode.Created, (await requester.PostAsJsonAsync(ClaimsUrl, new { storeId = scenario.FirstStoreId, claimDate = TestDate, clientTransactionId = Guid.NewGuid(), variantId = item.VariantId, quantity = 1, reasonCode = "LOST", note = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claimId } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.PostAsJsonAsync($"{ClaimApprovalsUrl}/{claimId}/reject", new { reason = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync(ReportUrl("reports/product-claims"))).StatusCode);

        var approverWithoutLegacy = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ClaimApproval.View, PermissionCodes.Products.ClaimApproval.Approve);
        Assert.Equal(HttpStatusCode.Forbidden, (await approverWithoutLegacy.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claimId } })).StatusCode);

        var otherStoreUser = await scenario.SignInUserWithPermissionsAsync(EFootwear.Domain.Security.RoleCodes.StoreManager, [secondStoreId],
            PermissionCodes.Products.ClaimApproval.View, PermissionCodes.Products.ClaimApproval.Approve, PermissionCodes.RestrictedOperations.Stock.ClaimApprove);
        Assert.True((await otherStoreUser.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claimId } })).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.False((await otherCompany.OwnerClient.PostAsJsonAsync(ApproveUrl, new { claimIds = new[] { claimId } })).IsSuccessStatusCode);
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task The_claim_report_lists_the_claims_of_the_period_with_their_total()
    {
        var (scenario, item) = await NewShopAsync();
        await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 2);
        await CreateClaimAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 3);

        var report = await GetJsonAsync(scenario.OwnerClient, $"{ReportUrl("reports/product-claims")}?storeId={scenario.FirstStoreId}&fromDate={TestDate}&toDate={TestDate}");
        Assert.Equal(2, report.GetProperty("rows").GetArrayLength());
        Assert.Equal(5, report.GetProperty("totalQuantity").GetDecimal());
        Assert.Equal(2_000, report.GetProperty("totalValueAmount").GetInt64());

        var empty = await GetJsonAsync(scenario.OwnerClient, $"{ReportUrl("reports/product-claims")}?fromDate=2025-01-01&toDate=2025-01-31");
        Assert.Equal(0, empty.GetProperty("rows").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportUrl("reports/product-claims")}?fromDate=2026-02-01&toDate=2026-01-01")).StatusCode);
    }
}
