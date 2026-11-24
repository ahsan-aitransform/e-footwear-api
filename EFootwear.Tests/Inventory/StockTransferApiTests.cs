using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;
using static EFootwear.Tests.Inventory.InventoryTestHelpers;

namespace EFootwear.Tests.Inventory;

/// <summary>
/// Transfers between stores with scan-based receiving (Part 19; docx 5.9, 5.10): the stock at every step, partial receiving and the loss cases, the decision of
/// each loss (found, charged to each bearer), the safe retry of scans, and the isolation of stores and companies.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class StockTransferApiTests(ApiTestEnvironment environment)
{
    private sealed record Shop(OwnerCompanyScenario Scenario, Guid SecondStoreId, Guid ThirdStoreId, CatalogueLookups Lookups, InventoryItem Item);

    private async Task<Shop> NewShopAsync(decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var thirdStoreId = await scenario.CreateStoreAsync("S3");
        var lookups = await LookupsAsync(scenario);
        return new Shop(scenario, secondStoreId, thirdStoreId, lookups, await CreateItemAsync(environment, scenario, lookups, "Gents Sandal", stock));
    }

    private Task<decimal> StockOf(Shop shop, Guid storeId) => StockAsync(environment, shop.Scenario.CompanyId, storeId, shop.Item.VariantId);

    [Fact]
    public async Task A_transfer_moves_stock_in_three_steps_and_partial_receiving_raises_a_loss_case_for_the_missing_piece()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;

        var transfer = await CreateTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId);
        var transferId = transfer.GetProperty("id").GetGuid();
        Assert.Equal("DRAFT", transfer.GetProperty("statusCode").GetString());
        Assert.Equal(1, transfer.GetProperty("transferNumber").GetInt64());

        var scan = await SendScanAsync(owner, transferId, shop.Item.Barcode, 4);
        Assert.Equal(4, scan.GetProperty("quantityScanned").GetDecimal());
        Assert.Equal(10, await StockOf(shop, shop.Scenario.FirstStoreId));

        var dispatched = await DispatchAsync(owner, transferId);
        Assert.Equal("SHIPPED", dispatched.GetProperty("statusCode").GetString());
        Assert.Equal(4, dispatched.GetProperty("quantityShipped").GetDecimal());
        Assert.Equal(6, await StockOf(shop, shop.Scenario.FirstStoreId));
        Assert.Equal(0, await StockOf(shop, shop.SecondStoreId));

        var received = await ReceiveScanAsync(owner, transferId, shop.Item.Barcode, 3);
        Assert.Equal(3, received.GetProperty("quantityReceived").GetDecimal());
        Assert.Equal(0, await StockOf(shop, shop.SecondStoreId));

        var completed = await CompleteReceivingAsync(owner, transferId);
        Assert.Equal("COMPLETED_WITH_MISSING", completed.GetProperty("statusCode").GetString());
        Assert.Equal(3, completed.GetProperty("quantityReceived").GetDecimal());
        Assert.Equal(1, completed.GetProperty("quantityMissing").GetDecimal());
        Assert.Equal(3, await StockOf(shop, shop.SecondStoreId));
        var lossCase = completed.GetProperty("lossCases")[0];
        Assert.Equal("OPEN", lossCase.GetProperty("statusCode").GetString());
        Assert.Equal(1, lossCase.GetProperty("quantity").GetDecimal());
        Assert.Equal(0, await JournalDifferenceAsync(environment, shop.Scenario.CompanyId));
    }

    [Fact]
    public async Task A_transfer_received_completely_completes_without_a_loss_case_and_the_total_stock_is_unchanged()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var transferId = await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 5);

        await ReceiveScanAsync(owner, transferId, shop.Item.Barcode, 5);
        var completed = await CompleteReceivingAsync(owner, transferId);

        Assert.Equal("COMPLETED", completed.GetProperty("statusCode").GetString());
        Assert.Equal(0, completed.GetProperty("lossCases").GetArrayLength());
        Assert.Equal(5, await StockOf(shop, shop.Scenario.FirstStoreId));
        Assert.Equal(5, await StockOf(shop, shop.SecondStoreId));
    }

    [Fact]
    public async Task Sending_the_same_scan_id_twice_counts_the_pieces_once()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var transferId = (await CreateTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId)).GetProperty("id").GetGuid();
        var batchId = Guid.NewGuid();

        var first = await SendScanAsync(owner, transferId, shop.Item.Barcode, 3, batchId);
        var repeated = await SendScanAsync(owner, transferId, shop.Item.Barcode, 3, batchId);

        Assert.False(first.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.True(repeated.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.Equal(3, repeated.GetProperty("quantityScanned").GetDecimal());
        var detail = await GetJsonAsync(owner, TransferUrl(transferId));
        Assert.Equal(3, detail.GetProperty("quantityScanned").GetDecimal());
    }

    [Fact]
    public async Task Every_loss_decision_works_found_goes_into_the_receivers_stock_and_a_charge_goes_to_each_bearer()
    {
        var shop = await NewShopAsync(stock: 20);
        var owner = shop.Scenario.OwnerClient;

        async Task<Guid> OpenLossCaseAsync()
        {
            var transferId = await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 2);
            await ReceiveScanAsync(owner, transferId, shop.Item.Barcode, 1);
            return (await CompleteReceivingAsync(owner, transferId)).GetProperty("lossCases")[0].GetProperty("id").GetGuid();
        }

        var foundCase = await OpenLossCaseAsync();
        var stockBefore = await StockOf(shop, shop.SecondStoreId);
        var found = await OwnerCompanyScenario.PostJsonAsync(owner, $"{DiscrepanciesUrl}/{foundCase}/decide", new { decisionCode = "FOUND", bearerCode = (string?)null });
        Assert.Equal("FOUND", found.GetProperty("statusCode").GetString());
        Assert.Equal(stockBefore + 1, await StockOf(shop, shop.SecondStoreId));

        foreach (var bearer in new[] { "COMPANY", "SENDER_STORE", "RECEIVER_STORE" })
        {
            var lossCaseId = await OpenLossCaseAsync();
            var stockWhileOpen = await StockOf(shop, shop.SecondStoreId);
            var charged = await OwnerCompanyScenario.PostJsonAsync(owner, $"{DiscrepanciesUrl}/{lossCaseId}/decide", new { decisionCode = "CHARGE", bearerCode = bearer });
            Assert.Equal(bearer, charged.GetProperty("bearerCode").GetString());
            Assert.NotEqual("OPEN", charged.GetProperty("statusCode").GetString());
            Assert.Equal(stockWhileOpen, await StockOf(shop, shop.SecondStoreId));
        }

        Assert.Equal(0, await JournalDifferenceAsync(environment, shop.Scenario.CompanyId));
        var open = await GetJsonAsync(owner, $"{DiscrepanciesUrl}?statusCode=OPEN");
        Assert.Equal(0, open.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_loss_decision_needs_a_bearer_for_a_charge_and_cannot_be_made_twice()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var transferId = await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 2);
        var lossCaseId = (await CompleteReceivingAsync(owner, transferId)).GetProperty("lossCases")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{DiscrepanciesUrl}/{lossCaseId}/decide", new { decisionCode = "CHARGE", bearerCode = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{DiscrepanciesUrl}/{lossCaseId}/decide", new { decisionCode = "SHRUG", bearerCode = (string?)null })).StatusCode);
        await OwnerCompanyScenario.PostJsonAsync(owner, $"{DiscrepanciesUrl}/{lossCaseId}/decide", new { decisionCode = "CHARGE", bearerCode = "COMPANY" });
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{DiscrepanciesUrl}/{lossCaseId}/decide", new { decisionCode = "FOUND", bearerCode = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Receiving_more_pieces_than_were_sent_is_refused()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var transferId = await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 2);

        var response = await owner.PostAsJsonAsync($"{ReceivingUrl}/{transferId}/scans", new { barcode = shop.Item.Barcode, quantity = 3, scanBatchId = Guid.NewGuid() });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, (await GetJsonAsync(owner, TransferUrl(transferId))).GetProperty("quantityReceived").GetDecimal());
    }

    [Fact]
    public async Task A_draft_cannot_be_dispatched_empty_and_a_cancelled_draft_takes_no_more_scans()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var transferId = (await CreateTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{TransferUrl(transferId)}/dispatch", new { })).StatusCode);
        var cancelled = await OwnerCompanyScenario.PostJsonAsync(owner, $"{TransferUrl(transferId)}/cancel", new { reason = "Changed mind" });
        Assert.Equal("CANCELLED", cancelled.GetProperty("statusCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{TransferUrl(transferId)}/send-scans", new { barcode = shop.Item.Barcode, quantity = 1, scanBatchId = Guid.NewGuid() })).StatusCode);

        var shipped = await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{TransferUrl(shipped)}/cancel", new { reason = "Too late" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{TransferUrl(shipped)}/send-scans", new { barcode = shop.Item.Barcode, quantity = 1, scanBatchId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task A_transfer_to_the_same_store_or_a_store_of_another_company_is_refused()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        var other = await OwnerCompanyScenario.CreateAsync(environment);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(TransfersUrl, new { fromStoreId = shop.Scenario.FirstStoreId, toStoreId = shop.Scenario.FirstStoreId, clientTransactionId = Guid.NewGuid(), note = (string?)null })).StatusCode);
        Assert.False((await owner.PostAsJsonAsync(TransfersUrl, new { fromStoreId = shop.Scenario.FirstStoreId, toStoreId = other.FirstStoreId, clientTransactionId = Guid.NewGuid(), note = (string?)null })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_sending_and_the_receiving_store_may_only_do_their_own_part()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        string[] senderPermissions = [PermissionCodes.Invoice.StockTransfer.View, PermissionCodes.Invoice.StockTransfer.Add, PermissionCodes.Invoice.StockTransfer.Edit, PermissionCodes.RestrictedOperations.Stock.TransferCreate];
        string[] receiverPermissions = [PermissionCodes.Inventory.StockTransferReceiving.View, PermissionCodes.Inventory.StockTransferReceiving.Add, PermissionCodes.Inventory.StockTransferReceiving.Edit, PermissionCodes.RestrictedOperations.Stock.TransferReceive];
        var sender = await shop.Scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [shop.Scenario.FirstStoreId], senderPermissions);
        var receiver = await shop.Scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [shop.SecondStoreId], receiverPermissions);
        var bystander = await shop.Scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [shop.ThirdStoreId], senderPermissions.Concat(receiverPermissions).ToArray());

        var transferId = (await CreateTransferAsync(sender, shop.Scenario.FirstStoreId, shop.SecondStoreId)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsJsonAsync($"{TransferUrl(transferId)}/send-scans", new { barcode = shop.Item.Barcode, quantity = 1, scanBatchId = Guid.NewGuid() })).StatusCode);
        Assert.True((await bystander.PostAsJsonAsync($"{TransferUrl(transferId)}/send-scans", new { barcode = shop.Item.Barcode, quantity = 1, scanBatchId = Guid.NewGuid() })).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True((await bystander.GetAsync(TransferUrl(transferId))).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        await SendScanAsync(sender, transferId, shop.Item.Barcode, 2);
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsJsonAsync($"{TransferUrl(transferId)}/dispatch", new { })).StatusCode);
        await DispatchAsync(sender, transferId);

        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsJsonAsync($"{ReceivingUrl}/{transferId}/scans", new { barcode = shop.Item.Barcode, quantity = 1, scanBatchId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsJsonAsync($"{ReceivingUrl}/{transferId}/complete", new { })).StatusCode);
        Assert.True((await bystander.PostAsJsonAsync($"{ReceivingUrl}/{transferId}/complete", new { })).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        await ReceiveScanAsync(receiver, transferId, shop.Item.Barcode, 2);
        var completed = await CompleteReceivingAsync(receiver, transferId);
        Assert.Equal("COMPLETED", completed.GetProperty("statusCode").GetString());
        Assert.Equal(2, await StockOf(shop, shop.SecondStoreId));

        var incoming = await GetJsonAsync(receiver, $"{ReceivingUrl}?storeId={shop.SecondStoreId}");
        Assert.Equal(1, incoming.GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.GetAsync(TransfersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsJsonAsync($"{DiscrepanciesUrl}/{Guid.NewGuid()}/decide", new { decisionCode = "FOUND", bearerCode = (string?)null })).StatusCode);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.OwnerClient.GetAsync(TransferUrl(transferId))).StatusCode);
        Assert.False((await otherCompany.OwnerClient.PostAsJsonAsync($"{TransferUrl(transferId)}/dispatch", new { })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(TransferUrl(transferId))).StatusCode);
    }

    [Fact]
    public async Task The_incoming_list_hides_drafts_and_the_transfer_list_filters_by_direction_and_status()
    {
        var shop = await NewShopAsync();
        var owner = shop.Scenario.OwnerClient;
        await CreateTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId);
        await DispatchedTransferAsync(owner, shop.Scenario.FirstStoreId, shop.SecondStoreId, shop.Item, 1);

        var incoming = await GetJsonAsync(owner, $"{ReceivingUrl}?storeId={shop.SecondStoreId}");
        Assert.Equal(1, incoming.GetProperty("totalCount").GetInt64());
        var outgoing = await GetJsonAsync(owner, $"{TransfersUrl}?storeId={shop.Scenario.FirstStoreId}&directionCode=OUTGOING");
        Assert.Equal(2, outgoing.GetProperty("totalCount").GetInt64());
        var incomingOfSender = await GetJsonAsync(owner, $"{TransfersUrl}?storeId={shop.Scenario.FirstStoreId}&directionCode=INCOMING");
        Assert.Equal(0, incomingOfSender.GetProperty("totalCount").GetInt64());
        var drafts = await GetJsonAsync(owner, $"{TransfersUrl}?statusCode=DRAFT");
        Assert.Equal(1, drafts.GetProperty("totalCount").GetInt64());
        var byNumber = await GetJsonAsync(owner, $"{TransfersUrl}?transferNumber=2");
        Assert.Equal(1, byNumber.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_multi_store_transfer_creates_one_transfer_per_destination_and_a_retry_moves_nothing_twice()
    {
        var shop = await NewShopAsync(stock: 20);
        var owner = shop.Scenario.OwnerClient;
        var body = new
        {
            fromStoreId = shop.Scenario.FirstStoreId, note = "Eid stock", dispatch = true,
            destinations = new[]
            {
                new { toStoreId = shop.SecondStoreId, clientTransactionId = Guid.NewGuid(), lines = new[] { new { barcode = shop.Item.Barcode, quantity = 3 } } },
                new { toStoreId = shop.ThirdStoreId, clientTransactionId = Guid.NewGuid(), lines = new[] { new { barcode = shop.Item.Barcode, quantity = 5 } } },
            },
        };

        var created = await OwnerCompanyScenario.PostJsonAsync(owner, $"{TransfersUrl}/multi", body);
        var transfers = created.GetProperty("transfers");
        Assert.Equal(2, transfers.GetArrayLength());
        Assert.All(transfers.EnumerateArray(), transfer => Assert.Equal("SHIPPED", transfer.GetProperty("statusCode").GetString()));
        Assert.Equal(12, await StockOf(shop, shop.Scenario.FirstStoreId));

        var retry = await OwnerCompanyScenario.PostJsonAsync(owner, $"{TransfersUrl}/multi", body);
        Assert.Equal(transfers[0].GetProperty("id").GetGuid(), retry.GetProperty("transfers")[0].GetProperty("id").GetGuid());
        Assert.Equal(12, await StockOf(shop, shop.Scenario.FirstStoreId));

        var sameStoreTwice = new { body.fromStoreId, body.note, body.dispatch, destinations = new[] { body.destinations[0], body.destinations[0] } };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{TransfersUrl}/multi", sameStoreTwice)).StatusCode);
        var toItself = new { body.fromStoreId, body.note, body.dispatch, destinations = new[] { new { toStoreId = shop.Scenario.FirstStoreId, clientTransactionId = Guid.NewGuid(), lines = body.destinations[0].lines } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{TransfersUrl}/multi", toItself)).StatusCode);
    }

    [Fact]
    public async Task A_multi_store_transfer_with_a_line_that_cannot_be_sent_stores_nothing_for_any_destination()
    {
        var shop = await NewShopAsync(stock: 4);
        var owner = shop.Scenario.OwnerClient;
        var body = new
        {
            fromStoreId = shop.Scenario.FirstStoreId, note = (string?)null, dispatch = true,
            destinations = new[]
            {
                new { toStoreId = shop.SecondStoreId, clientTransactionId = Guid.NewGuid(), lines = new[] { new { barcode = shop.Item.Barcode, quantity = 3 } } },
                new { toStoreId = shop.ThirdStoreId, clientTransactionId = Guid.NewGuid(), lines = new[] { new { barcode = shop.Item.Barcode, quantity = 3 } } },
            },
        };

        var response = await owner.PostAsJsonAsync($"{TransfersUrl}/multi", body);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(4, await StockOf(shop, shop.Scenario.FirstStoreId));
        Assert.Equal(0, (await GetJsonAsync(owner, TransfersUrl)).GetProperty("totalCount").GetInt64());
    }
}
