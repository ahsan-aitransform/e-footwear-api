using System.Net;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Payroll.PayrollTestHelpers;

namespace EFootwear.Tests.Payroll;

/// <summary>The Staff Salary and Salary Outstanding reports: totals reconcile with the salary sheets, filters work, stores are isolated.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PayrollReportApiTests(ApiTestEnvironment environment)
{
    [Fact]
    public async Task The_staff_salary_report_adds_up_to_the_salary_sheets_and_filters_by_store_and_employee()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        var firstStaffId = await CreateStaffAsync(environment, scenario, "Imran Manager", scenario.FirstStoreId, 20_000);
        await CreateStaffAsync(environment, scenario, "Zaman Salesman", scenario.FirstStoreId, 30_000);
        await CreateStaffAsync(environment, scenario, "Mubeen Cashier", secondStoreId, 18_000);
        await AddAdjustmentAsync(scenario.OwnerClient, firstStaffId, "INCENTIVE", 4_000);
        var firstSheet = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        await PrepareSheetAsync(scenario.OwnerClient, secondStoreId);
        await ApproveSheetAsync(scenario.OwnerClient, firstSheet.GetProperty("runId").GetGuid());
        await PaySalaryAsync(scenario.OwnerClient, LineIdOf(firstSheet, firstStaffId), 10_000);

        var everything = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffSalaryReportUrl(null)));
        Assert.Equal(3, everything.GetProperty("rows").GetArrayLength());
        Assert.Equal(3, everything.GetProperty("lineCount").GetInt64());
        Assert.Equal(68_000, everything.GetProperty("totalBaseSalary").GetInt64());
        Assert.Equal(4_000, everything.GetProperty("totalIncentive").GetInt64());
        Assert.Equal(72_000, everything.GetProperty("totalNetPay").GetInt64());
        Assert.Equal(10_000, everything.GetProperty("totalPaid").GetInt64());
        Assert.Equal(62_000, everything.GetProperty("totalOutstanding").GetInt64());
        Assert.False(everything.GetProperty("isTruncated").GetBoolean());

        var firstStoreOnly = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffSalaryReportUrl(scenario.FirstStoreId)));
        Assert.Equal(2, firstStoreOnly.GetProperty("rows").GetArrayLength());
        Assert.Equal(firstSheet.GetProperty("totalNetPay").GetInt64(), firstStoreOnly.GetProperty("totalNetPay").GetInt64());

        var oneEmployee = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            StaffSalaryReportUrl(scenario.FirstStoreId) + $"&staffId={firstStaffId}"));
        var row = Assert.Single(oneEmployee.GetProperty("rows").EnumerateArray());
        Assert.Equal(24_000, row.GetProperty("netPay").GetInt64());
        Assert.Equal(14_000, row.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal("APPROVED", row.GetProperty("statusCode").GetString());

        var otherMonth = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffSalaryReportUrl(null, "2026-11-01", "2026-11-01")));
        Assert.Equal(0, otherMonth.GetProperty("lineCount").GetInt64());
        Assert.Equal(0, otherMonth.GetProperty("totalNetPay").GetInt64());
    }

    [Fact]
    public async Task A_rebuilt_draft_never_counts_twice_in_the_staff_salary_report()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateStaffAsync(environment, scenario, "Rebuilt Staff", scenario.FirstStoreId, 20_000);
        await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);

        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(StaffSalaryReportUrl(scenario.FirstStoreId)));

        Assert.Equal(1, report.GetProperty("lineCount").GetInt64());
        Assert.Equal(20_000, report.GetProperty("totalNetPay").GetInt64());
    }

    [Fact]
    public async Task The_salary_outstanding_report_lists_only_approved_salary_that_is_not_paid_in_full()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var firstStaffId = await CreateStaffAsync(environment, scenario, "Part Paid", scenario.FirstStoreId, 20_000);
        var secondStaffId = await CreateStaffAsync(environment, scenario, "Fully Paid", scenario.FirstStoreId, 15_000);

        var draft = await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var draftReport = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SalaryOutstandingReportUrl(null)));
        Assert.Equal(0, draftReport.GetProperty("lineCount").GetInt64());   // a draft is not owed yet

        var approved = await ApproveSheetAsync(scenario.OwnerClient, draft.GetProperty("runId").GetGuid());
        var approvedReport = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SalaryOutstandingReportUrl(null)));
        Assert.Equal(2, approvedReport.GetProperty("lineCount").GetInt64());
        Assert.Equal(35_000, approvedReport.GetProperty("totalOutstanding").GetInt64());

        await PaySalaryAsync(scenario.OwnerClient, LineIdOf(approved, firstStaffId), 5_000);
        await PaySalaryAsync(scenario.OwnerClient, LineIdOf(approved, secondStaffId), 15_000);
        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SalaryOutstandingReportUrl(scenario.FirstStoreId)));

        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal("Part Paid", row.GetProperty("employeeName").GetString());
        Assert.Equal(20_000, row.GetProperty("netPay").GetInt64());
        Assert.Equal(5_000, row.GetProperty("paidAmount").GetInt64());
        Assert.Equal(15_000, row.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal(15_000, report.GetProperty("totalOutstanding").GetInt64());
    }

    [Fact]
    public async Task A_report_needs_its_own_permission_refuses_a_bad_month_range_and_other_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("ST2");
        await CreateStaffAsync(environment, scenario, "First Store Staff", scenario.FirstStoreId, 20_000);
        await CreateStaffAsync(environment, scenario, "Second Store Staff", secondStoreId, 25_000);
        await ApproveSheetAsync(scenario.OwnerClient, (await PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("runId").GetGuid());
        await ApproveSheetAsync(scenario.OwnerClient, (await PrepareSheetAsync(scenario.OwnerClient, secondStoreId)).GetProperty("runId").GetGuid());

        var nobody = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId]);
        var reader = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Reports.StaffSalaryReport.View, PermissionCodes.Reports.SalaryOutstandingReport.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(StaffSalaryReportUrl(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SalaryOutstandingReportUrl(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(StaffSalaryReportUrl(null))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync(StaffSalaryReportUrl(null, "2026-12-01", "2026-10-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(StaffSalaryReportUrl(secondStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(SalaryOutstandingReportUrl(secondStoreId))).StatusCode);

        // With no store chosen the store user still sees only their own store.
        var visible = await OwnerCompanyScenario.ReadJsonAsync(await reader.GetAsync(StaffSalaryReportUrl(null)));
        Assert.Equal(1, visible.GetProperty("lineCount").GetInt64());
        Assert.Equal(20_000, visible.GetProperty("totalNetPay").GetInt64());
        var outstanding = await OwnerCompanyScenario.ReadJsonAsync(await reader.GetAsync(SalaryOutstandingReportUrl(null)));
        Assert.Equal(20_000, outstanding.GetProperty("totalOutstanding").GetInt64());
    }

    [Fact]
    public async Task Another_company_sees_nothing_in_the_payroll_reports()
    {
        var firstCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var secondCompany = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateStaffAsync(environment, firstCompany, "Company One Staff", firstCompany.FirstStoreId, 20_000);
        await ApproveSheetAsync(firstCompany.OwnerClient, (await PrepareSheetAsync(firstCompany.OwnerClient, firstCompany.FirstStoreId)).GetProperty("runId").GetGuid());

        var salary = await OwnerCompanyScenario.ReadJsonAsync(await secondCompany.OwnerClient.GetAsync(StaffSalaryReportUrl(null)));
        var outstanding = await OwnerCompanyScenario.ReadJsonAsync(await secondCompany.OwnerClient.GetAsync(SalaryOutstandingReportUrl(null)));

        Assert.Equal(0, salary.GetProperty("lineCount").GetInt64());
        Assert.Equal(0, outstanding.GetProperty("lineCount").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await secondCompany.OwnerClient.GetAsync(StaffSalaryReportUrl(firstCompany.FirstStoreId))).StatusCode);
    }
}
