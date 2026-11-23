using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Accounting;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Parties;
using static EFootwear.Tests.Payroll.PayrollTestHelpers;

namespace EFootwear.Tests.Payroll;

/// <summary>
/// The monthly salary sheet, payroll adjustments and salary payments (docx 3.8, 3.9; Part 16.3). The payroll rules live in the database functions; these tests
/// check that the API drives them correctly: nothing is paid by approving, payments are partial or full and never exceed what is owed, a retried payment
/// is recorded once, and stores and companies stay isolated. Money is whole rupees.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class PayrollSheetApiTests(ApiTestEnvironment environment)
{
    private static readonly string[] SalaryPermissionCodes =
    [
        PermissionCodes.Party.NewSalarySheet.View, PermissionCodes.Party.NewSalarySheet.Add, PermissionCodes.Party.NewSalarySheet.Edit,
        PermissionCodes.Party.ViewSalarySheet.View,
        PermissionCodes.HumanResources.PayrollRuns.Approve,
        PermissionCodes.HumanResources.SalaryPayments.View, PermissionCodes.HumanResources.SalaryPayments.Add,
        PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollPrepare, PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollApprove,
    ];

    [Fact]
    public async Task A_sheet_has_one_line_per_staff_member_and_net_pay_is_base_plus_incentive_minus_deduction()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var firstStaffId = await CreateStaffAsync(environment, scenario, "Imran Manager", scenario.FirstStoreId, 20_000, "Manager");
        var secondStaffId = await CreateStaffAsync(environment, scenario, "Zaman Salesman", scenario.FirstStoreId, 30_000);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);   // nothing built yet

        var sheet = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        Assert.Equal("DRAFT", sheet.GetProperty("statusCode").GetString());
        Assert.Equal(SalaryMonth, sheet.GetProperty("periodMonth").GetString());
        Assert.Equal(2, sheet.GetProperty("lines").GetArrayLength());
        Assert.Equal(20_000, LineOf(sheet, firstStaffId).GetProperty("netPay").GetInt64());
        Assert.Equal(30_000, LineOf(sheet, secondStaffId).GetProperty("baseSalary").GetInt64());
        Assert.Equal(50_000, sheet.GetProperty("totalNetPay").GetInt64());
        Assert.Equal(50_000, sheet.GetProperty("totalOutstanding").GetInt64());
        Assert.Equal(0, sheet.GetProperty("totalPaid").GetInt64());

        // An incentive and a deduction wait as adjustments until the draft is built again.
        await AddAdjustmentAsync(scenario.OwnerClient, firstStaffId, "INCENTIVE", 5_000, "Eid bonus");
        await AddAdjustmentAsync(scenario.OwnerClient, firstStaffId, "DEDUCTION", 2_000, "Absence");
        await AddAdjustmentAsync(scenario.OwnerClient, firstStaffId, "ADVANCE", 1_000);
        var adjustments = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(AdjustmentsOfStoreUrl(scenario.FirstStoreId)));
        Assert.Equal(3, adjustments.GetArrayLength());
        Assert.All(adjustments.EnumerateArray(), adjustment => Assert.False(adjustment.GetProperty("isOnSalarySheet").GetBoolean()));

        var rebuilt = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        Assert.Equal(sheet.GetProperty("runId").GetGuid(), rebuilt.GetProperty("runId").GetGuid());   // the same sheet, built again
        var line = LineOf(rebuilt, firstStaffId);
        Assert.Equal(5_000, line.GetProperty("incentiveAmount").GetInt64());
        Assert.Equal(3_000, line.GetProperty("deductionAmount").GetInt64());   // deduction plus advance
        Assert.Equal(22_000, line.GetProperty("netPay").GetInt64());
        Assert.Equal(52_000, rebuilt.GetProperty("totalNetPay").GetInt64());
        var onSheet = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(AdjustmentsOfStoreUrl(scenario.FirstStoreId)));
        Assert.All(onSheet.EnumerateArray(), adjustment => Assert.True(adjustment.GetProperty("isOnSalarySheet").GetBoolean()));

        var read = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SheetUrl(scenario.FirstStoreId)));
        Assert.Equal(2, read.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Approving_posts_a_balanced_salary_expense_and_pays_nothing()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Imran Manager", scenario.FirstStoreId, 20_000);
        await CreateStaffAsync(environment, scenario, "Zaman Salesman", scenario.FirstStoreId, 30_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);

        var approved = await ApproveSheetAsync(scenario.OwnerClient, draft.GetProperty("runId").GetGuid());

        Assert.Equal("APPROVED", approved.GetProperty("statusCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(approved.GetProperty("approvedByName").GetString()));
        Assert.Equal(0, approved.GetProperty("totalPaid").GetInt64());   // a sheet is never paid by approving it
        Assert.Equal(50_000, approved.GetProperty("totalOutstanding").GetInt64());
        Assert.All(approved.GetProperty("lines").EnumerateArray(), line => Assert.Equal("APPROVED", line.GetProperty("statusCode").GetString()));
        Assert.Equal(20_000, LineOf(approved, staffId).GetProperty("outstandingAmount").GetInt64());

        var (debit, credit) = await JournalTotalsOfSourceAsync(environment, scenario.CompanyId, "PAYROLL");
        Assert.Equal(50_000, debit);
        Assert.Equal(50_000, credit);
        Assert.Equal(0, await VoucherTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(0, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from hr.salary_payments where company_id = $1", scenario.CompanyId));
    }

    [Fact]
    public async Task An_approved_sheet_is_final_it_cannot_be_approved_again_rebuilt_or_get_new_adjustments()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Final Sheet Staff", scenario.FirstStoreId, 20_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var runId = draft.GetProperty("runId").GetGuid();
        await ApproveSheetAsync(scenario.OwnerClient, runId);

        var approveAgain = await scenario.OwnerClient.PostAsync(ApproveSheetUrl(runId), null);
        Assert.Equal(HttpStatusCode.Conflict, approveAgain.StatusCode);

        var rebuild = await scenario.OwnerClient.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = SalaryMonth });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rebuild.StatusCode);   // the database refuses to rebuild anything but a draft

        var lateAdjustment = await scenario.OwnerClient.PostAsJsonAsync(AdjustmentsUrl,
            new { employeePartyId = staffId, month = SalaryMonth, adjustmentTypeCode = "INCENTIVE", amount = 1_000, reason = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, lateAdjustment.StatusCode);
    }

    [Fact]
    public async Task Salary_is_paid_in_parts_the_line_turns_paid_at_zero_and_a_payment_never_exceeds_what_is_owed()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Paid In Parts", scenario.FirstStoreId, 20_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var approved = await ApproveSheetAsync(scenario.OwnerClient, draft.GetProperty("runId").GetGuid());
        var lineId = LineIdOf(approved, staffId);

        var first = await PaySalaryAsync(scenario.OwnerClient, lineId, 8_000, "CASH");
        Assert.Equal(8_000, first.GetProperty("amount").GetInt64());
        Assert.Equal("CASH", first.GetProperty("methodCode").GetString());
        Assert.Equal("Paid In Parts", first.GetProperty("employeeName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("paidByName").GetString()));

        var afterFirst = LineOf(await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SheetUrl(scenario.FirstStoreId))), staffId);
        Assert.Equal(8_000, afterFirst.GetProperty("paidAmount").GetInt64());
        Assert.Equal(12_000, afterFirst.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal("APPROVED", afterFirst.GetProperty("statusCode").GetString());

        var tooMuch = await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(12_001));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        await PaySalaryAsync(scenario.OwnerClient, lineId, 12_000, "BANK");
        var afterLast = LineOf(await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SheetUrl(scenario.FirstStoreId))), staffId);
        Assert.Equal(20_000, afterLast.GetProperty("paidAmount").GetInt64());
        Assert.Equal(0, afterLast.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal("PAID", afterLast.GetProperty("statusCode").GetString());

        var paidInFull = await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1));
        Assert.Equal(HttpStatusCode.Conflict, paidInFull.StatusCode);

        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(LinePaymentsUrl(lineId)));
        Assert.Equal(2, history.GetArrayLength());
        var (debit, credit) = await JournalTotalsOfSourceAsync(environment, scenario.CompanyId, "SALARY_PAYMENT");
        Assert.Equal(20_000, debit);
        Assert.Equal(20_000, credit);
        Assert.Equal(0, await VoucherTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));

        var allPayments = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PaymentsUrl}?storeId={scenario.FirstStoreId}"));
        Assert.Equal(2, allPayments.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Salary_cannot_be_paid_before_the_sheet_is_approved_and_bad_payments_are_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Not Yet Approved", scenario.FirstStoreId, 20_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var lineId = LineIdOf(draft, staffId);

        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1_000))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(-5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1_000, "CHEQUE"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(Guid.NewGuid()), PaymentBody(1_000))).StatusCode);
        Assert.Equal(0, await SalaryPaymentCountAsync(environment, scenario.CompanyId, lineId));
    }

    [Fact]
    public async Task A_retried_payment_with_the_same_client_transaction_id_is_recorded_once()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Retry Staff", scenario.FirstStoreId, 20_000);
        var approved = await ApproveSheetAsync(scenario.OwnerClient, (await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("runId").GetGuid());
        var lineId = LineIdOf(approved, staffId);
        var clientTransactionId = Guid.NewGuid();

        var first = await PaySalaryAsync(scenario.OwnerClient, lineId, 20_000, "CASH", clientTransactionId);
        var retry = await PaySalaryAsync(scenario.OwnerClient, lineId, 20_000, "CASH", clientTransactionId);   // the line is already paid in full: still the first answer

        Assert.Equal(first.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        Assert.Equal(1, await SalaryPaymentCountAsync(environment, scenario.CompanyId, lineId));
        var line = LineOf(await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SheetUrl(scenario.FirstStoreId))), staffId);
        Assert.Equal(20_000, line.GetProperty("paidAmount").GetInt64());
        var (debit, credit) = await JournalTotalsOfSourceAsync(environment, scenario.CompanyId, "SALARY_PAYMENT");
        Assert.Equal(20_000, debit);
        Assert.Equal(20_000, credit);
    }

    [Fact]
    public async Task Bad_adjustments_are_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Adjusted Staff", scenario.FirstStoreId, 20_000);
        var customerId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Not Staff Customer");

        async Task<HttpStatusCode> Post(Guid employeePartyId, string type, long amount, string? reason = null) =>
            (await scenario.OwnerClient.PostAsJsonAsync(AdjustmentsUrl, new { employeePartyId, month = SalaryMonth, adjustmentTypeCode = type, amount, reason })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(staffId, "INCENTIVE", 0));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staffId, "INCENTIVE", -10));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staffId, "BONUS", 100));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staffId, "INCENTIVE", 100, new string('x', 301)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(customerId, "INCENTIVE", 100));   // a customer is not staff
        Assert.Equal(HttpStatusCode.Created, await Post(staffId, "INCENTIVE", 100, "Fine"));
    }

    [Fact]
    public async Task A_month_must_be_a_real_month_and_a_store_is_required()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var farFuture = await scenario.OwnerClient.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = "2301-01-01" });
        var headOffice = await scenario.OwnerClient.PostAsJsonAsync(PrepareSheetUrl, new { storeId = Guid.Empty, month = SalaryMonth });
        // Any day of the month is accepted and moved to the first.
        var midMonth = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId, "2026-10-17");

        Assert.Equal(HttpStatusCode.BadRequest, farFuture.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, headOffice.StatusCode);
        Assert.Equal(SalaryMonth, midMonth.GetProperty("periodMonth").GetString());
    }

    [Fact]
    public async Task A_sheet_lists_the_staff_of_the_store_where_each_person_works_at_the_time_it_is_built()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var stayingId = await CreateStaffAsync(environment, scenario, "Staying Staff", scenario.FirstStoreId, 10_000);
        var movingId = await CreateStaffAsync(environment, scenario, "Moving Staff", scenario.FirstStoreId, 12_000);
        await scenario.OwnerClient.PostAsJsonAsync($"{PartyTestHelpers.PartiesUrl}/{movingId}/staff-transfers", new { toStoreId = secondStoreId, effectiveOn = "2026-10-15" });

        var firstStoreSheet = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var secondStoreSheet = await PrepareSheetAsync(scenario.OwnerClient, secondStoreId);

        Assert.Equal(stayingId, Assert.Single(firstStoreSheet.GetProperty("lines").EnumerateArray()).GetProperty("employeePartyId").GetGuid());
        Assert.Equal(movingId, Assert.Single(secondStoreSheet.GetProperty("lines").EnumerateArray()).GetProperty("employeePartyId").GetGuid());
    }

    [Fact]
    public async Task Every_payroll_action_needs_its_screen_permission_and_the_legacy_payroll_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Permission Staff", scenario.FirstStoreId, 20_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var runId = draft.GetProperty("runId").GetGuid();
        var lineId = LineIdOf(draft, staffId);

        var nobody = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId]);
        var screenOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Party.NewSalarySheet.Add, PermissionCodes.Party.NewSalarySheet.Edit, PermissionCodes.HumanResources.PayrollRuns.Approve,
            PermissionCodes.HumanResources.SalaryPayments.Add);   // without PAYROLL_PREPARE / PAYROLL_APPROVE
        var legacyOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollPrepare, PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollApprove);
        var accountant = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], SalaryPermissionCodes);

        // A person with no permission cannot do anything.
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(PaymentsUrl)).StatusCode);

        // The screen permission alone is not enough, and neither is the legacy code alone.
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacyOnly.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsync(ApproveSheetUrl(runId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacyOnly.PostAsync(ApproveSheetUrl(runId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1_000))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsJsonAsync(AdjustmentsUrl,
            new { employeePartyId = staffId, month = SalaryMonth, adjustmentTypeCode = "INCENTIVE", amount = 100 })).StatusCode);

        // Both together work for the steps that stay inside the person's own store.
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync(PrepareSheetUrl, new { storeId = scenario.FirstStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await accountant.PostAsJsonAsync(AdjustmentsUrl,
            new { employeePartyId = staffId, month = SalaryMonth, adjustmentTypeCode = "INCENTIVE", amount = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);

        // The company owner (who has every permission and may work in every store) approves and pays.
        var approveResponse = await scenario.OwnerClient.PostAsync(ApproveSheetUrl(runId), null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        // The accountant prepared the sheet again, which cancelled the first line and made a new one: the old line id can no longer be paid.
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1_000))).StatusCode);
        var approvedLineId = LineIdOf(await OwnerCompanyScenario.ReadJsonAsync(approveResponse), staffId);
        Assert.Equal(HttpStatusCode.Created, (await scenario.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(approvedLineId), PaymentBody(1_000))).StatusCode);

        // A platform administrator is not a company user.
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);
    }

    [Fact]
    public async Task A_store_limited_user_cannot_approve_or_pay_because_both_post_to_the_head_office_even_with_every_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, scenario, "Head Office Rule Staff", scenario.FirstStoreId, 20_000);
        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var storeManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], SalaryPermissionCodes);

        var approve = await storeManager.PostAsync(ApproveSheetUrl(draft.GetProperty("runId").GetGuid()), null);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Contains("head office", (await TestScenarioBuilder.ReadProblemAsync(approve)).GetProperty("detail").GetString());

        var approved = await ApproveSheetAsync(scenario.OwnerClient, draft.GetProperty("runId").GetGuid());
        var pay = await storeManager.PostAsJsonAsync(LinePaymentsUrl(LineIdOf(approved, staffId)), PaymentBody(1_000));
        Assert.Equal(HttpStatusCode.Forbidden, pay.StatusCode);
        Assert.Contains("head office", (await TestScenarioBuilder.ReadProblemAsync(pay)).GetProperty("detail").GetString());
        Assert.Equal(0, await SalaryPaymentCountAsync(environment, scenario.CompanyId, LineIdOf(approved, staffId)));
    }

    [Fact]
    public async Task A_store_user_cannot_see_or_change_the_salary_sheet_or_payments_of_another_store()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        await CreateStaffAsync(environment, scenario, "First Store Staff", scenario.FirstStoreId, 20_000);
        var otherStaffId = await CreateStaffAsync(environment, scenario, "Second Store Staff", secondStoreId, 25_000);
        var secondStoreSheet = await PrepareSheetAsync(scenario.OwnerClient, secondStoreId);
        var approvedSecondStore = await ApproveSheetAsync(scenario.OwnerClient, secondStoreSheet.GetProperty("runId").GetGuid());
        var otherLineId = LineIdOf(approvedSecondStore, otherStaffId);
        await PaySalaryAsync(scenario.OwnerClient, otherLineId, 5_000);
        var firstStoreManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], SalaryPermissionCodes);

        Assert.Equal(HttpStatusCode.Forbidden, (await firstStoreManager.GetAsync(SheetUrl(secondStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await firstStoreManager.PostAsJsonAsync(PrepareSheetUrl, new { storeId = secondStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await firstStoreManager.GetAsync(AdjustmentsOfStoreUrl(secondStoreId))).StatusCode);
        // The other store's sheet and line are not even visible to this person: row-level security answers "not found".
        Assert.Equal(HttpStatusCode.NotFound, (await firstStoreManager.PostAsync(ApproveSheetUrl(secondStoreSheet.GetProperty("runId").GetGuid()), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstStoreManager.PostAsJsonAsync(LinePaymentsUrl(otherLineId), PaymentBody(1_000))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstStoreManager.GetAsync(LinePaymentsUrl(otherLineId))).StatusCode);
        var adjustmentForOtherStore = await firstStoreManager.PostAsJsonAsync(AdjustmentsUrl,
            new { employeePartyId = otherStaffId, month = SalaryMonth, adjustmentTypeCode = "INCENTIVE", amount = 100 });
        Assert.Equal(HttpStatusCode.Forbidden, adjustmentForOtherStore.StatusCode);

        var visiblePayments = await OwnerCompanyScenario.ReadJsonAsync(await firstStoreManager.GetAsync(PaymentsUrl));
        Assert.Equal(0, visiblePayments.GetProperty("totalCount").GetInt64());
        Assert.Equal(1, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PaymentsUrl))).GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Another_company_cannot_reach_a_salary_sheet_line_or_payment_by_changing_ids()
    {
        var firstCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var secondCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffAsync(environment, firstCompany, "Company One Staff", firstCompany.FirstStoreId, 20_000);
        var approved = await ApproveSheetAsync(firstCompany.OwnerClient, (await PrepareSheetAsync(firstCompany.OwnerClient, firstCompany.FirstStoreId)).GetProperty("runId").GetGuid());
        var lineId = LineIdOf(approved, staffId);
        var runId = approved.GetProperty("runId").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.GetAsync(SheetUrl(firstCompany.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.PostAsJsonAsync(PrepareSheetUrl, new { storeId = firstCompany.FirstStoreId, month = SalaryMonth })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.PostAsync(ApproveSheetUrl(runId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.PostAsJsonAsync(LinePaymentsUrl(lineId), PaymentBody(1_000))).StatusCode);
        Assert.Equal(0, await SalaryPaymentCountAsync(environment, firstCompany.CompanyId, lineId));
    }
}
