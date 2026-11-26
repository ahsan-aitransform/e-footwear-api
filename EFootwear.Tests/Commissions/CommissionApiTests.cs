using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Payroll;
using EFootwear.Tests.Sales;
using static EFootwear.Tests.Commissions.CommissionTestHelpers;

namespace EFootwear.Tests.Commissions;

/// <summary>
/// Commissions (Part 16.2): a plan for one salesman ("this salesman gets 3 percent"), the commission that accrues on his sales, its reversal on a return,
/// approval, the two reports, the refusals and the permission matrix. Money is whole rupees everywhere.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class CommissionApiTests(ApiTestEnvironment environment)
{
    private static readonly string[] AllCommissionPermissions =
    [
        PermissionCodes.Commissions.CommissionPlans.View, PermissionCodes.Commissions.CommissionPlans.Add, PermissionCodes.Commissions.CommissionPlans.Edit,
        PermissionCodes.Commissions.CommissionPlans.Delete, PermissionCodes.Commissions.CommissionEntries.View, PermissionCodes.Commissions.CommissionEntries.Approve,
        PermissionCodes.RestrictedOperations.ExpensesAndPayroll.CommissionApprove, PermissionCodes.Reports.CommissionReport.View,
        PermissionCodes.Reports.CommissionAccrualAndPaymentReport.View,
    ];

    [Fact]
    public async Task A_plan_for_one_salesman_earns_three_percent_of_each_of_his_sales_and_nothing_for_other_salesmen()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var otherSalesmanId = await PayrollTestHelpers.CreateStaffAsync(environment, scenario, "Other Salesman", scenario.FirstStoreId, 0);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        Assert.Equal(1, plan.GetProperty("memberCount").GetInt32());

        await PostSaleBySalesmanAsync(environment, scenario, item, 2, salesmanId);     // 2 000 of sales
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);     // 1 000 more
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, otherSalesmanId);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, null);

        var entries = await ListEntriesAsync(scenario.OwnerClient);
        Assert.Equal(2, entries.GetProperty("totalCount").GetInt64());
        Assert.All(entries.GetProperty("items").EnumerateArray(), entry =>
        {
            Assert.Equal(salesmanId, entry.GetProperty("staffId").GetGuid());
            Assert.Equal("EARN", entry.GetProperty("entryTypeCode").GetString());
            Assert.Equal("PENDING", entry.GetProperty("statusCode").GetString());
            Assert.Equal(3m, entry.GetProperty("rate").GetDecimal());
        });
        var totals = await GetTotalsAsync(scenario.OwnerClient);
        Assert.Equal(90, totals.GetProperty("totalAmount").GetInt64());      // 3 percent of 3 000
        Assert.Equal(90, totals.GetProperty("pendingAmount").GetInt64());
        Assert.Equal(0, totals.GetProperty("approvedAmount").GetInt64());
    }

    [Fact]
    public async Task Selling_the_same_sale_again_with_the_same_client_transaction_id_accrues_the_commission_once()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 5m);
        var clientTransactionId = Guid.NewGuid();

        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId, clientTransactionId);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId, clientTransactionId);

        Assert.Equal(1, await EntryCountInDatabaseAsync(environment, scenario.CompanyId));
        Assert.Equal(50, (await GetTotalsAsync(scenario.OwnerClient)).GetProperty("totalAmount").GetInt64());
    }

    [Fact]
    public async Task A_fixed_amount_per_piece_pays_for_every_piece_sold()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreatePlanAsync(scenario.OwnerClient, PlanBody("Per piece", [salesmanId], calculationType: "FIXED_PER_PIECE", fixedAmount: 40));

        await PostSaleBySalesmanAsync(environment, scenario, item, 3, salesmanId);

        Assert.Equal(120, (await GetTotalsAsync(scenario.OwnerClient)).GetProperty("totalAmount").GetInt64());
    }

    [Fact]
    public async Task A_plan_for_a_whole_role_pays_every_salesman_and_a_member_plan_wins_over_it()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var otherSalesmanId = await PayrollTestHelpers.CreateStaffAsync(environment, scenario, "Other Salesman", scenario.FirstStoreId, 0);
        await CreatePlanAsync(scenario.OwnerClient, PlanBody("Everybody", rate: 1m));
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 4m, "Zaman only");

        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, otherSalesmanId);

        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId)));
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(40, rows.Single(row => row.GetProperty("staffId").GetGuid() == salesmanId).GetProperty("netAmount").GetInt64());
        Assert.Equal(10, rows.Single(row => row.GetProperty("staffId").GetGuid() == otherSalesmanId).GetProperty("netAmount").GetInt64());
    }

    [Fact]
    public async Task A_return_takes_the_commission_back_and_the_net_is_zero()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var invoice = await PostSaleBySalesmanAsync(environment, scenario, item, 2, salesmanId);

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SalesTestHelpers.SaleReturnsUrl,
            SalesTestHelpers.ReturnBody(scenario.FirstStoreId, [SalesTestHelpers.ReturnedLine(SalesTestHelpers.FirstLineId(invoice), 2)]));

        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId)));
        Assert.Equal(60, report.GetProperty("totalEarnedAmount").GetInt64());
        Assert.Equal(-60, report.GetProperty("totalReversedAmount").GetInt64());
        Assert.Equal(0, report.GetProperty("totalNetAmount").GetInt64());
        var entries = await ListEntriesAsync(scenario.OwnerClient);
        Assert.Equal(2, entries.GetProperty("totalCount").GetInt64());
        Assert.Contains(entries.GetProperty("items").EnumerateArray(), entry => entry.GetProperty("entryTypeCode").GetString() == "REVERSAL");
    }

    [Fact]
    public async Task A_voided_invoice_takes_its_commission_back()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var invoice = await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);

        var voidResponse = await scenario.OwnerClient.PostAsJsonAsync(SalesTestHelpers.VoidUrl(invoice.GetProperty("id").GetGuid()), new { reason = "Entered by mistake" });
        await TestScenarioBuilder.EnsureSuccessAsync(voidResponse);

        Assert.Equal(0, (await GetTotalsAsync(scenario.OwnerClient)).GetProperty("totalAmount").GetInt64());
    }

    [Fact]
    public async Task Approving_turns_pending_commission_into_approved_commission_and_is_safe_to_repeat()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        await PostSaleBySalesmanAsync(environment, scenario, item, 2, salesmanId);

        var first = await ApproveAsync(scenario.OwnerClient, scenario.FirstStoreId);
        Assert.Equal(1, first.GetProperty("approvedEntryCount").GetInt32());
        var second = await ApproveAsync(scenario.OwnerClient, scenario.FirstStoreId);
        Assert.Equal(0, second.GetProperty("approvedEntryCount").GetInt32());

        var totals = await GetTotalsAsync(scenario.OwnerClient);
        Assert.Equal(0, totals.GetProperty("pendingAmount").GetInt64());
        Assert.Equal(60, totals.GetProperty("approvedAmount").GetInt64());
        Assert.Equal(0, totals.GetProperty("paidAmount").GetInt64());
    }

    [Fact]
    public async Task Approved_commission_goes_onto_the_salary_sheet_and_becomes_paid_when_the_salary_is_paid()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        await PostSaleBySalesmanAsync(environment, scenario, item, 2, salesmanId);
        await ApproveAsync(scenario.OwnerClient, scenario.FirstStoreId);

        // The sale was made in 2026-11, so that month's sheet takes the commission.
        var draft = await PayrollTestHelpers.PrepareSheetAsync(scenario.OwnerClient, scenario.FirstStoreId, "2026-11-01");
        Assert.Equal(60, PayrollTestHelpers.LineOf(draft, salesmanId).GetProperty("commissionAmount").GetInt64());
        var approved = await PayrollTestHelpers.ApproveSheetAsync(scenario.OwnerClient, draft.GetProperty("runId").GetGuid());
        await PayrollTestHelpers.PaySalaryAsync(scenario.OwnerClient, PayrollTestHelpers.LineIdOf(approved, salesmanId), 60);

        var totals = await GetTotalsAsync(scenario.OwnerClient);
        Assert.Equal(60, totals.GetProperty("paidAmount").GetInt64());
        Assert.Equal(0, totals.GetProperty("approvedAmount").GetInt64());
        var accrual = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(AccrualReportUrl(scenario.FirstStoreId)));
        Assert.Equal(60, accrual.GetProperty("totalPaidAmount").GetInt64());
        var entry = (await ListEntriesAsync(scenario.OwnerClient, "statusCode=PAID")).GetProperty("items")[0];
        Assert.Equal("2026-11-01", entry.GetProperty("salaryMonth").GetString());
    }

    [Fact]
    public async Task The_summary_and_detail_reports_agree_and_can_be_limited_to_one_salesman()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var otherSalesmanId = await PayrollTestHelpers.CreateStaffAsync(environment, scenario, "Other Salesman", scenario.FirstStoreId, 0);
        await CreatePlanAsync(scenario.OwnerClient, PlanBody("Everybody", rate: 2m));
        await PostSaleBySalesmanAsync(environment, scenario, item, 3, salesmanId);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, otherSalesmanId);

        var summary = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId)));
        var detail = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(DetailReportUrl(scenario.FirstStoreId)));
        Assert.Equal(2, summary.GetProperty("staffCount").GetInt64());
        Assert.Equal(80, summary.GetProperty("totalNetAmount").GetInt64());
        Assert.Equal(summary.GetProperty("totalNetAmount").GetInt64(), detail.GetProperty("totalCommissionAmount").GetInt64());
        Assert.Equal(4_000, summary.GetProperty("totalBaseAmount").GetInt64());
        Assert.Equal(2, detail.GetProperty("entryCount").GetInt64());

        var one = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId, staffId: salesmanId)));
        Assert.Equal(1, one.GetProperty("rows").GetArrayLength());
        Assert.Equal(60, one.GetProperty("totalNetAmount").GetInt64());

        var otherMonth = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId, "2026-10-01", "2026-10-31")));
        Assert.Equal(0, otherMonth.GetProperty("rows").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync(SummaryReportUrl(scenario.FirstStoreId, "2026-11-30", "2026-11-01"))).StatusCode);
    }

    [Fact]
    public async Task A_plan_that_is_not_active_or_has_ended_pays_nothing_and_a_new_plan_does_not_rewrite_old_entries()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);   // earns 30 at 3 percent

        var planId = plan.GetProperty("id").GetGuid();
        var changed = await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId), PlanBody("Salesman plan", [salesmanId], 10m, rowVersion: plan.GetProperty("rowVersion").GetInt64()));
        await TestScenarioBuilder.EnsureSuccessAsync(changed);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);   // earns 100 at 10 percent

        var rates = (await ListEntriesAsync(scenario.OwnerClient)).GetProperty("items").EnumerateArray()
            .Select(entry => (entry.GetProperty("rate").GetDecimal(), entry.GetProperty("commissionAmount").GetInt64())).OrderBy(pair => pair.Item1).ToList();
        Assert.Equal([(3m, 30L), (10m, 100L)], rates);

        var current = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PlanUrl(planId)));
        var deactivated = await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId), PlanBody("Salesman plan", [salesmanId], 10m, isActive: false, rowVersion: current.GetProperty("rowVersion").GetInt64()));
        await TestScenarioBuilder.EnsureSuccessAsync(deactivated);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);
        Assert.Equal(2, (await ListEntriesAsync(scenario.OwnerClient)).GetProperty("totalCount").GetInt64());   // nothing new accrued
    }

    [Fact]
    public async Task A_rule_gives_one_product_its_own_rate_and_cannot_overlap_another_rule_of_the_same_product()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var planId = plan.GetProperty("id").GetGuid();
        var rule = new
        {
            scopeCode = "PRODUCT", productId = item.ProductId, categoryId = (Guid?)null, priceBandId = (Guid?)null, calculationTypeCode = "PERCENT",
            rate = 8m, fixedAmountPerPiece = (long?)null, minimumLineAmount = 0L, priority = 0, validFrom = "2026-01-01", validTo = (string?)null,
        };

        var withRule = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PlanRulesUrl(planId), rule);
        Assert.Equal(1, withRule.GetProperty("ruleCount").GetInt32());
        Assert.Equal(item.ProductId, withRule.GetProperty("rules")[0].GetProperty("productId").GetGuid());
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);
        Assert.Equal(80, (await GetTotalsAsync(scenario.OwnerClient)).GetProperty("totalAmount").GetInt64());   // 8 percent, not 3

        var overlapping = await scenario.OwnerClient.PostAsJsonAsync(PlanRulesUrl(planId), rule);
        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
    }

    [Fact]
    public async Task A_rule_can_be_ended_but_not_before_it_started()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var planId = plan.GetProperty("id").GetGuid();
        var withRule = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PlanRulesUrl(planId), new
        {
            scopeCode = "PRODUCT", productId = item.ProductId, categoryId = (Guid?)null, priceBandId = (Guid?)null, calculationTypeCode = "PERCENT",
            rate = 8m, fixedAmountPerPiece = (long?)null, minimumLineAmount = 0L, priority = 0, validFrom = "2026-06-01", validTo = (string?)null,
        });
        var ruleId = withRule.GetProperty("rules")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(EndRuleUrl(planId, ruleId), new { validTo = "2026-05-31" })).StatusCode);
        var ended = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, EndRuleUrl(planId, ruleId), new { validTo = "2026-10-31" });
        Assert.Equal("2026-10-31", ended.GetProperty("rules")[0].GetProperty("validTo").GetString());

        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);   // November: the rule has ended, the plan default pays
        Assert.Equal(30, (await GetTotalsAsync(scenario.OwnerClient)).GetProperty("totalAmount").GetInt64());
    }

    [Fact]
    public async Task A_plan_is_checked_before_it_is_saved()
    {
        var (scenario, _, salesmanId) = await NewShopWithSalesmanAsync(environment);

        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("", [salesmanId]));                                      // no name
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Too much", [salesmanId], rate: 101m));                  // above 100 percent
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Negative", [salesmanId], rate: -1m));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Decimals", [salesmanId], rate: 1.23456m));               // more than four decimals
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("No rate", [salesmanId], rate: null));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("No amount", [salesmanId], calculationType: "FIXED_PER_PIECE", fixedAmount: null));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Bad role", [salesmanId], appliesTo: "CLEANER"));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Bad dates", [salesmanId], validFrom: "2026-05-01", validTo: "2026-04-01"));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Unknown person", [Guid.NewGuid()]));
        await AssertBadRequestAsync(scenario.OwnerClient, PlanBody("Negative minimum", [salesmanId], minimumStoreSales: -5));
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PlansUrl))).GetArrayLength());
    }

    [Fact]
    public async Task Two_people_changing_the_same_plan_cannot_overwrite_each_other()
    {
        var (scenario, _, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var planId = plan.GetProperty("id").GetGuid();
        var version = plan.GetProperty("rowVersion").GetInt64();

        var first = await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId), PlanBody("First change", [salesmanId], 4m, rowVersion: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId), PlanBody("Second change", [salesmanId], 5m, rowVersion: version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var withoutVersion = await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId), PlanBody("No version", [salesmanId], 5m));
        Assert.Equal(HttpStatusCode.BadRequest, withoutVersion.StatusCode);

        var current = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PlanUrl(planId)));
        Assert.Equal("First change", current.GetProperty("name").GetString());
        Assert.Equal(4m, current.GetProperty("defaultRate").GetDecimal());
    }

    [Fact]
    public async Task The_members_of_a_plan_can_be_replaced_and_a_deleted_plan_leaves_the_list_but_keeps_its_entries()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var otherSalesmanId = await PayrollTestHelpers.CreateStaffAsync(environment, scenario, "Other Salesman", scenario.FirstStoreId, 0);
        var plan = await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        var planId = plan.GetProperty("id").GetGuid();
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);

        var replaced = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PutAsJsonAsync(PlanUrl(planId),
            PlanBody("Salesman plan", [otherSalesmanId], 3m, rowVersion: plan.GetProperty("rowVersion").GetInt64())));
        Assert.Equal(otherSalesmanId, replaced.GetProperty("members")[0].GetProperty("staffId").GetGuid());
        Assert.Equal(1, replaced.GetProperty("members").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync(PlanUrl(planId))).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PlansUrl))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync(PlanUrl(planId))).StatusCode);
        Assert.Equal(1, (await ListEntriesAsync(scenario.OwnerClient)).GetProperty("totalCount").GetInt64());   // the commission already earned stays
    }

    [Fact]
    public async Task Commission_screens_need_their_own_permissions_and_approving_needs_the_restricted_code_too()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);

        var nobody = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId]);
        var viewer = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Commissions.CommissionPlans.View, PermissionCodes.Commissions.CommissionEntries.View);
        var screenOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Commissions.CommissionEntries.View, PermissionCodes.Commissions.CommissionEntries.Approve);
        var restrictedOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.RestrictedOperations.ExpensesAndPayroll.CommissionApprove);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(PlansUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(EntriesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SummaryReportUrl(scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(AccrualReportUrl(scenario.FirstStoreId))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(PlansUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(EntriesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(PlansUrl, PlanBody("No right", [salesmanId]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(ApproveUrl, new { storeId = scenario.FirstStoreId, fromDate = "2026-11-01", toDate = "2026-11-30" })).StatusCode);

        var body = new { storeId = scenario.FirstStoreId, fromDate = "2026-11-01", toDate = "2026-11-30" };
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsJsonAsync(ApproveUrl, body)).StatusCode);        // without COMMISSION_APPROVE
        Assert.Equal(HttpStatusCode.Forbidden, (await restrictedOnly.PostAsJsonAsync(ApproveUrl, body)).StatusCode);    // without the screen's Approve
        Assert.Equal(1, (await ListEntriesAsync(scenario.OwnerClient, "statusCode=PENDING")).GetProperty("totalCount").GetInt64());

        var approver = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], AllCommissionPermissions);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync(ApproveUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.GetAsync(SummaryReportUrl(scenario.FirstStoreId))).StatusCode);
    }

    [Fact]
    public async Task A_person_of_one_store_cannot_see_or_approve_the_commission_of_another_store_and_cannot_change_a_plan_of_every_store()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("SECOND");
        await CreatePlanAsync(scenario.OwnerClient, PlanBody("Everybody", rate: 3m));
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);
        var manager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId], AllCommissionPermissions);

        var seen = await ListEntriesAsync(manager);
        Assert.Equal(0, seen.GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{EntriesUrl}?storeId={scenario.FirstStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(SummaryReportUrl(scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync(ApproveUrl, new { storeId = scenario.FirstStoreId, fromDate = "2026-11-01", toDate = "2026-11-30" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(PlansUrl, PlanBody("For everybody", rate: 2m))).StatusCode);   // a plan of every store
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync(PlansUrl, PlanBody("For my store", rate: 2m, storeId: secondStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(PlansUrl, PlanBody("For the other store", rate: 2m, storeId: scenario.FirstStoreId))).StatusCode);
    }

    [Fact]
    public async Task Another_company_never_sees_the_plans_or_the_entries_of_this_company()
    {
        var (scenario, item, salesmanId) = await NewShopWithSalesmanAsync(environment);
        await CreateSalesmanPlanAsync(scenario.OwnerClient, salesmanId, 3m);
        await PostSaleBySalesmanAsync(environment, scenario, item, 1, salesmanId);
        var strangers = await OwnerCompanyScenario.CreateAsync(environment);

        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await strangers.OwnerClient.GetAsync(PlansUrl))).GetArrayLength());
        Assert.Equal(0, (await ListEntriesAsync(strangers.OwnerClient)).GetProperty("totalCount").GetInt64());
        Assert.Equal(0, (await GetTotalsAsync(strangers.OwnerClient)).GetProperty("totalAmount").GetInt64());
        var summary = await OwnerCompanyScenario.ReadJsonAsync(await strangers.OwnerClient.GetAsync(SummaryReportUrl(null)));
        Assert.Equal(0, summary.GetProperty("rows").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await strangers.OwnerClient.GetAsync(PlanUrl(Guid.NewGuid()))).StatusCode);
    }

    private static async Task AssertBadRequestAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(PlansUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
