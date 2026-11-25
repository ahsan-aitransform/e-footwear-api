using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Accounting;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Contacts;
using static EFootwear.Tests.Payroll.PayrollTestHelpers;

namespace EFootwear.Tests.HumanResources;

/// <summary>
/// Staff profiles, salary terms with their history, the store history of staff and the Staff Transfers list (docx 3.1 staff, Part 16.1).
/// Money is whole rupees. Stores and companies are isolated.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class StaffApiTests(ApiTestEnvironment environment)
{
    [Fact]
    public async Task Salary_terms_are_saved_with_a_history_row_and_a_stale_save_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Imran Manager", scenario.FirstStoreId, baseSalary: 0, designation: "Manager");

        var before = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}/{staffId}"));
        var staleRowVersion = before.GetProperty("rowVersion").GetInt64();
        Assert.Equal(0, before.GetProperty("profile").GetProperty("baseSalary").GetInt64());

        var saved = await scenario.OwnerClient.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms",
            new { baseSalary = 20_000, payFrequencyCode = "MONTHLY", rowVersion = staleRowVersion });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var after = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}/{staffId}"));
        Assert.Equal(20_000, after.GetProperty("profile").GetProperty("baseSalary").GetInt64());
        Assert.Equal("Manager", after.GetProperty("profile").GetProperty("designation").GetString());
        var salaryTerms = after.GetProperty("salaryTerms").EnumerateArray().ToList();
        Assert.Contains(salaryTerms, term => term.GetProperty("baseSalary").GetInt64() == 20_000 && term.GetProperty("payFrequencyCode").GetString() == "MONTHLY");

        // The same row version a second time: someone else changed the row in between.
        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms",
            new { baseSalary = 25_000, payFrequencyCode = "MONTHLY", rowVersion = staleRowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENT_UPDATE", await VoucherTestHelpers.ErrorCodeOfAsync(stale));
    }

    [Fact]
    public async Task Bad_salary_terms_are_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Bad Terms Staff", scenario.FirstStoreId, baseSalary: 10_000);
        var rowVersion = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}/{staffId}"))).GetProperty("rowVersion").GetInt64();

        var negative = await scenario.OwnerClient.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms", new { baseSalary = -1, payFrequencyCode = "MONTHLY", rowVersion });
        var unknownFrequency = await scenario.OwnerClient.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms", new { baseSalary = 5_000, payFrequencyCode = "DAILY", rowVersion });
        var notStaff = await scenario.OwnerClient.PutAsJsonAsync($"{StaffUrl}/{Guid.NewGuid()}/salary-terms", new { baseSalary = 5_000, payFrequencyCode = "MONTHLY", rowVersion });

        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownFrequency.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notStaff.StatusCode);
    }

    [Fact]
    public async Task The_staff_list_filters_by_store_and_text_and_hides_former_staff_unless_asked()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var firstStaffId = await CreateStaffAsync(environment, scenario, "Zaman Salesman", scenario.FirstStoreId, 20_000);
        await CreateStaffAsync(environment, scenario, "Mubeen Cashier", secondStoreId, 18_000, designation: "Cashier");

        var everyone = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffUrl));
        Assert.Equal(2, everyone.GetProperty("totalCount").GetInt64());

        var secondStoreOnly = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}?storeId={secondStoreId}"));
        Assert.Equal("Mubeen Cashier", Assert.Single(secondStoreOnly.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());

        var searched = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}?searchText=zaman"));
        Assert.Equal(firstStaffId, Assert.Single(searched.GetProperty("items").EnumerateArray()).GetProperty("staffId").GetGuid());

        await environment.Database.ExecuteAsOwnerAsync(
            "update contacts.staff set left_on = date '2026-09-30' where company_id = $1 and id = $2", scenario.CompanyId, firstStaffId);
        var withoutFormer = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffUrl));
        var withFormer = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}?includeFormerStaff=true"));
        Assert.Equal(1, withoutFormer.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, withFormer.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_store_user_sees_and_changes_only_the_staff_of_their_own_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var ownStoreStaffId = await CreateStaffAsync(environment, scenario, "Own Store Staff", scenario.FirstStoreId, 15_000);
        var otherStoreStaffId = await CreateStaffAsync(environment, scenario, "Other Store Staff", secondStoreId, 15_000);
        var manager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.HumanResources.StaffProfiles.View, PermissionCodes.HumanResources.SalaryTerms.Edit);

        var list = await OwnerCompanyScenario.ReadJsonAsync(await manager.GetAsync(StaffUrl));
        Assert.Equal(ownStoreStaffId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("staffId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"{StaffUrl}/{ownStoreStaffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{StaffUrl}/{otherStoreStaffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{StaffUrl}?storeId={secondStoreId}")).StatusCode);
        var changeOther = await manager.PutAsJsonAsync($"{StaffUrl}/{otherStoreStaffId}/salary-terms",
            new { baseSalary = 99_000, payFrequencyCode = "MONTHLY", rowVersion = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, changeOther.StatusCode);
    }

    [Fact]
    public async Task A_transfer_between_stores_keeps_the_history_with_previous_store_new_store_date_and_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var staffId = await CreateStaffAsync(environment, scenario, "Moving Staff", scenario.FirstStoreId, 20_000);

        var transfer = await scenario.OwnerClient.PostAsJsonAsync($"{ContactTestHelpers.StaffMembersUrl}/{staffId}/transfers",
            new { toStoreId = secondStoreId, effectiveOn = "2026-10-15" });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        var detail = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffUrl}/{staffId}"));
        Assert.Equal(secondStoreId, detail.GetProperty("profile").GetProperty("currentStoreId").GetGuid());
        var history = detail.GetProperty("transferHistory").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);   // the first assignment when the person was created, then the move
        var move = history.Single(item => item.GetProperty("toStoreId").GetGuid() == secondStoreId);
        Assert.Equal(scenario.FirstStoreId, move.GetProperty("fromStoreId").GetGuid());
        Assert.Equal("2026-10-15", move.GetProperty("effectiveOn").GetString());
        Assert.False(string.IsNullOrWhiteSpace(move.GetProperty("changedByName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(move.GetProperty("fromStoreName").GetString()));

        var transfers = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffTransfersUrl}?staffId={staffId}"));
        Assert.Equal(2, transfers.GetProperty("totalCount").GetInt64());
        Assert.Equal("Moving Staff", transfers.GetProperty("items")[0].GetProperty("staffName").GetString());
    }

    [Fact]
    public async Task The_transfer_list_shows_a_store_user_only_transfers_that_touch_their_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var thirdStoreId = await scenario.CreateStoreAsync("ST3");
        var staffInSecond = await CreateStaffAsync(environment, scenario, "Second To Third", secondStoreId, 10_000);
        var staffInFirst = await CreateStaffAsync(environment, scenario, "First To Second", scenario.FirstStoreId, 10_000);
        await scenario.OwnerClient.PostAsJsonAsync($"{ContactTestHelpers.StaffMembersUrl}/{staffInSecond}/transfers", new { toStoreId = thirdStoreId, effectiveOn = "2026-10-10" });
        await scenario.OwnerClient.PostAsJsonAsync($"{ContactTestHelpers.StaffMembersUrl}/{staffInFirst}/transfers", new { toStoreId = secondStoreId, effectiveOn = "2026-10-12" });
        var firstStoreManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.HumanResources.StaffTransfers.View);

        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffTransfersUrl));
        var visible = await OwnerCompanyScenario.ReadJsonAsync(await firstStoreManager.GetAsync(StaffTransfersUrl));

        Assert.Equal(4, all.GetProperty("totalCount").GetInt64());   // two first assignments and two moves
        var visibleNames = visible.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("staffName").GetString()).ToList();
        Assert.All(visibleNames, name => Assert.Equal("First To Second", name));
        Assert.Equal(2, visible.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Staff_endpoints_need_their_permissions_and_refuse_a_platform_administrator()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Guarded Staff", scenario.FirstStoreId, 12_000);
        var nobody = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId]);
        var viewerOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.HumanResources.StaffProfiles.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(StaffUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"{StaffUrl}/{staffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(StaffTransfersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewerOnly.GetAsync(StaffUrl)).StatusCode);
        var viewerEdits = await viewerOnly.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms", new { baseSalary = 1, payFrequencyCode = "MONTHLY", rowVersion = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, viewerEdits.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(StaffUrl)).StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_read_a_staff_member_by_changing_the_id()
    {
        var firstCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var secondCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, firstCompany, "Company One Staff", firstCompany.FirstStoreId, 12_000);

        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.GetAsync($"{StaffUrl}/{staffId}")).StatusCode);
        var list = await OwnerCompanyScenario.ReadJsonAsync(await secondCompany.OwnerClient.GetAsync(StaffUrl));
        Assert.Equal(0, list.GetProperty("totalCount").GetInt64());
    }
}
