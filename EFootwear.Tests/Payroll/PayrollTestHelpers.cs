using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Contacts;

namespace EFootwear.Tests.Payroll;

/// <summary>Builders and database helpers shared by the staff, salary sheet, salary payment and payroll report tests.</summary>
public static class PayrollTestHelpers
{
    public const string SalaryMonth = "2026-10-01";
    public const string PaymentDate = "2026-11-05";

    public static readonly string StaffUrl = TestScenarioBuilder.Url(ApiRoutes.Staff.Root);
    public static readonly string PayrollUrl = TestScenarioBuilder.Url(ApiRoutes.Payroll.Root);

    public static string StaffTransfersUrl => $"{StaffUrl}/{ApiRoutes.Staff.Transfers}";
    public static string PrepareSheetUrl => $"{PayrollUrl}/{ApiRoutes.Payroll.PrepareSheet}";
    public static string AdjustmentsUrl => $"{PayrollUrl}/{ApiRoutes.Payroll.Adjustments}";
    public static string PaymentsUrl => $"{PayrollUrl}/{ApiRoutes.Payroll.Payments}";

    public static string SheetUrl(Guid storeId, string month = SalaryMonth) => $"{PayrollUrl}/{ApiRoutes.Payroll.Sheet}?storeId={storeId}&month={month}";
    public static string ApproveSheetUrl(Guid runId) => $"{PayrollUrl}/sheets/{runId}/approve";
    public static string LinePaymentsUrl(Guid lineId) => $"{PayrollUrl}/lines/{lineId}/payments";
    public static string AdjustmentsOfStoreUrl(Guid storeId, string month = SalaryMonth) => $"{AdjustmentsUrl}?storeId={storeId}&month={month}";
    public static string StaffSalaryReportUrl(Guid? storeId, string fromMonth = SalaryMonth, string toMonth = SalaryMonth) =>
        $"{PayrollUrl}/{ApiRoutes.Payroll.StaffSalaryReport}?fromMonth={fromMonth}&toMonth={toMonth}" + (storeId is null ? "" : $"&storeId={storeId}");
    public static string SalaryOutstandingReportUrl(Guid? storeId) =>
        $"{PayrollUrl}/{ApiRoutes.Payroll.SalaryOutstandingReport}" + (storeId is null ? "" : $"?storeId={storeId}");

    /// <summary>Creates a staff member in a store and gives the person a base salary (whole rupees).</summary>
    public static async Task<Guid> CreateStaffAsync(
        ApiTestEnvironment environment, OwnerCompanyScenario scenario, string name, Guid storeId, long baseSalary, string designation = "Salesman")
    {
        var staffId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, ContactTestHelpers.StaffMembersUrl,
            ContactTestHelpers.StaffMemberBody(name, storeId, designation: designation))).GetProperty("id").GetGuid();
        // The staff module stamps today as the join date, and the database rightly leaves staff who joined after the salary month off the sheet.
        // The salary months of these tests are earlier than any day a test runs, so the person is made to have joined at the start of the year.
        await environment.Database.ExecuteAsOwnerAsync(
            "update contacts.staff set joined_on = date '2026-01-01' where company_id = $1 and id = $2", scenario.CompanyId, staffId);
        if (baseSalary > 0) await SetSalaryAsync(scenario.OwnerClient, staffId, baseSalary);
        return staffId;
    }

    public static async Task SetSalaryAsync(HttpClient client, Guid staffId, long baseSalary, string payFrequencyCode = "MONTHLY")
    {
        var detail = await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{StaffUrl}/{staffId}"));
        var response = await client.PutAsJsonAsync($"{StaffUrl}/{staffId}/salary-terms",
            new { baseSalary, payFrequencyCode, rowVersion = detail.GetProperty("rowVersion").GetInt64() });
        await TestScenarioBuilder.EnsureSuccessAsync(response);
    }

    public static Task<JsonElement> PrepareSheetAsync(HttpClient client, Guid storeId, string month = SalaryMonth) =>
        OwnerCompanyScenario.PostJsonAsync(client, PrepareSheetUrl, new { storeId, month });

    public static async Task<JsonElement> ApproveSheetAsync(HttpClient client, Guid runId)
    {
        var response = await client.PostAsync(ApproveSheetUrl(runId), null);
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await OwnerCompanyScenario.ReadJsonAsync(response);
    }

    public static Task<JsonElement> AddAdjustmentAsync(HttpClient client, Guid staffId, string adjustmentTypeCode, long amount, string? reason = null, string month = SalaryMonth) =>
        OwnerCompanyScenario.PostJsonAsync(client, AdjustmentsUrl, new { staffId, month, adjustmentTypeCode, amount, reason });

    public static object PaymentBody(long amount, string methodCode = "CASH", Guid? clientTransactionId = null, string? reference = null) =>
        new { amount, methodCode, paidOn = PaymentDate, reference, clientTransactionId };

    public static Task<JsonElement> PaySalaryAsync(HttpClient client, Guid lineId, long amount, string methodCode = "CASH", Guid? clientTransactionId = null) =>
        OwnerCompanyScenario.PostJsonAsync(client, LinePaymentsUrl(lineId), PaymentBody(amount, methodCode, clientTransactionId));

    /// <summary>The line of a sheet that belongs to the employee.</summary>
    public static JsonElement LineOf(JsonElement sheet, Guid staffId) =>
        sheet.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("staffId").GetGuid() == staffId);

    public static Guid LineIdOf(JsonElement sheet, Guid staffId) => LineOf(sheet, staffId).GetProperty("lineId").GetGuid();

    /// <summary>Total debit and total credit of every journal entry the payroll posted (source type), straight from the database.</summary>
    public static async Task<(long Debit, long Credit)> JournalTotalsOfSourceAsync(ApiTestEnvironment environment, Guid companyId, string sourceType)
    {
        var debit = await environment.Database.QueryScalarAsOwnerAsync<long>("""
            select cast(coalesce(sum(l.debit), 0) as bigint) from acct.journal_lines l
              join acct.journal_entries e on e.company_id = l.company_id and e.id = l.entry_id and e.entry_date = l.entry_date
             where e.company_id = $1 and e.source_type = $2
            """, companyId, sourceType);
        var credit = await environment.Database.QueryScalarAsOwnerAsync<long>("""
            select cast(coalesce(sum(l.credit), 0) as bigint) from acct.journal_lines l
              join acct.journal_entries e on e.company_id = l.company_id and e.id = l.entry_id and e.entry_date = l.entry_date
             where e.company_id = $1 and e.source_type = $2
            """, companyId, sourceType);
        return (debit, credit);
    }

    public static async Task<long> SalaryPaymentCountAsync(ApiTestEnvironment environment, Guid companyId, Guid lineId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from hr.salary_payments where company_id = $1 and payroll_line_id = $2", companyId, lineId);
}
