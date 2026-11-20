using System.Net;
using System.Text.Json;
using EFootwear.Api.Tests.Support;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class FinanceTests(ApiFixture fx)
{
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6];
    private static readonly string Today = DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static async Task<long> Id(Session s, string url, object body)
    {
        var (st, b) = await s.PostAsync(url, body);
        Assert.True(st == HttpStatusCode.Created, $"{url} -> {st} {b}");
        return b.GetProperty("id").GetInt64();
    }

    private sealed record Shop(Session Owner, long Vendor, long Variant, string Barcode, long Customer);

    private async Task<Shop> NewShopAsync()
    {
        var o = await fx.OwnerAsync();
        var dep = await Id(o, "/api/v1/masters/departments", new { name = U("D") });
        var cat = await Id(o, "/api/v1/masters/categories", new { name = U("C"), departmentId = dep });
        var vendor = await Id(o, "/api/v1/parties", new { name = U("Vendor"), isVendor = true });
        var cust = await Id(o, "/api/v1/parties", new { name = U("Cust"), isCustomer = true });
        var (st, b) = await o.PostAsync("/api/v1/catalog/products", new { name = U("Shoe"), departmentId = dep, categoryId = cat, variants = new[] { new { sizeCode = "8", purchasePrice = 1200, salePrice = 2000 } } });
        Assert.Equal(HttpStatusCode.Created, st);
        var v = (await o.GetJsonAsync($"/api/v1/catalog/products/{b.GetProperty("id").GetInt64()}")).GetProperty("variants")[0];
        return new Shop(o, vendor, v.GetProperty("id").GetInt64(), v.GetProperty("barcode").GetString()!, cust);
    }

    private static async Task<JsonElement> BuyAsync(Shop s, int qty = 10)
    {
        var (st, b) = await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, lines = new[] { new { variantId = s.Variant, qty, unitCost = 1200 } } });
        Assert.Equal(HttpStatusCode.Created, st);
        return b;
    }

    private static async Task<long> Balance(Session o, long party) => (await o.GetJsonAsync($"/api/v1/parties/{party}")).GetProperty("balance").GetInt64();

    private async Task AssertEntryBalanced(long entry)
        => Assert.Equal(0L, await fx.ScalarAsync<long>("select coalesce(sum(debit) - sum(credit), 0) from acct.journal_lines where company_id = @c and entry_id = @e", ("c", fx.Acme.CompanyId), ("e", entry)));

    private async Task<Session> UserAsync(Session owner, string role, params string[] perms)
    {
        var name = U("u");
        await Id(owner, "/api/v1/users", new { userName = name, fullName = name, role, password = "Start-Pass-1", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = perms });
        var s = await fx.LoginAsync("Acme Footwear", name, "Start-Pass-1");
        await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        return await fx.LoginAsync("Acme Footwear", name, "Next-Pass-22");
    }

    [Fact]
    public async Task Vendor_payment_settles_a_bill_over_payment_is_refused_and_voiding_puts_it_back()
    {
        var s = await NewShopAsync();
        var bill = await BuyAsync(s);
        var billId = bill.GetProperty("id").GetInt64();
        Assert.Equal(-12000, await Balance(s.Owner, s.Vendor));

        var pay = await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "PAYMENT", partyId = s.Vendor, amount = 5000, mode = "CASH", refNo = "CH-1", allocations = new[] { new { purchaseInvoiceId = billId, amount = 5000 } } });
        Assert.Equal(HttpStatusCode.Created, pay.Status);
        await AssertEntryBalanced(pay.Body.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(-7000, await Balance(s.Owner, s.Vendor));
        var open = await s.Owner.GetJsonAsync($"/api/v1/accounting/vendors/{s.Vendor}/open-bills");
        Assert.Equal(7000, open[0].GetProperty("remainingAmount").GetInt64());

        // cannot pay more than the bill has left, nor settle another vendor's bill, nor allocate more than the voucher
        var over = await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "PAYMENT", partyId = s.Vendor, amount = 8000, mode = "CASH", allocations = new[] { new { purchaseInvoiceId = billId, amount = 8000 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.Status);
        Assert.Contains("7000", over.Body.GetProperty("title").GetString());
        var other = await Id(s.Owner, "/api/v1/parties", new { name = U("Other"), isVendor = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "PAYMENT", partyId = other, amount = 100, mode = "CASH", allocations = new[] { new { purchaseInvoiceId = billId, amount = 100 } } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "PAYMENT", partyId = s.Vendor, amount = 100, mode = "CASH", allocations = new[] { new { purchaseInvoiceId = billId, amount = 200 } } })).Status);
        Assert.Equal(-7000, await Balance(s.Owner, s.Vendor));   // nothing leaked from the refused ones

        // void: balance and the bill's remaining go back
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/accounting/vouchers/{pay.Body.GetProperty("id").GetInt64()}/void", new { reason = "wrong cheque" })).Status);
        Assert.Equal(-12000, await Balance(s.Owner, s.Vendor));
        Assert.Equal(12000, (await s.Owner.GetJsonAsync($"/api/v1/accounting/vendors/{s.Vendor}/open-bills"))[0].GetProperty("remainingAmount").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/accounting/vouchers/{pay.Body.GetProperty("id").GetInt64()}/void", new { reason = "again" })).Status);

        // pay in full -> no open bill left
        await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "PAYMENT", partyId = s.Vendor, amount = 12000, mode = "BANK", allocations = new[] { new { purchaseInvoiceId = billId, amount = 12000 } } });
        Assert.Equal(0, await Balance(s.Owner, s.Vendor));
        Assert.Equal(0, (await s.Owner.GetJsonAsync($"/api/v1/accounting/vendors/{s.Vendor}/open-bills")).GetArrayLength());
    }

    [Fact]
    public async Task Customer_receipt_reduces_the_balance_the_ledger_shows_a_running_total_and_opening_balances_work()
    {
        var s = await NewShopAsync();
        await BuyAsync(s);
        await s.Owner.PostAsync("/api/v1/sales", new { customerId = s.Customer, lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = Array.Empty<object>() });   // 2000 on credit
        Assert.Equal(2000, await Balance(s.Owner, s.Customer));
        var rec = await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "RECEIPT", partyId = s.Customer, amount = 1500, mode = "CASH" });
        Assert.Equal(HttpStatusCode.Created, rec.Status);
        Assert.Equal(500, await Balance(s.Owner, s.Customer));

        var ledger = await s.Owner.GetJsonAsync($"/api/v1/accounting/parties/{s.Customer}/ledger?from={Today}&to={Today}");
        Assert.Equal(2, ledger.GetProperty("lines").GetArrayLength());
        Assert.Equal(2000, ledger.GetProperty("lines")[0].GetProperty("running").GetInt64());
        Assert.Equal(500, ledger.GetProperty("closing").GetInt64());

        // opening balance: a customer who already owed 3000; the difference goes to opening equity so the entry balances
        var c2 = await Id(s.Owner, "/api/v1/parties", new { name = U("Old"), isCustomer = true });
        var op = await s.Owner.PostAsync("/api/v1/accounting/opening-balances", new { items = new[] { new { partyId = (long?)c2, accountId = (long?)null, debit = 3000, credit = 0 } } });
        Assert.Equal(HttpStatusCode.OK, op.Status);
        Assert.Equal(3000, await Balance(s.Owner, c2));
        await AssertEntryBalanced(op.Body.GetProperty("entryId").GetInt64());

        // receipts need a party and a real mode; a journal between party-control accounts needs the party
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "RECEIPT", amount = 10, mode = "CASH" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "RECEIPT", partyId = s.Customer, amount = 10, mode = "BARTER" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/accounting/vouchers", new { type = "RECEIPT", partyId = s.Customer, amount = 0, mode = "CASH" })).Status);
    }

    [Fact]
    public async Task Expense_needs_a_second_person_to_approve_posts_to_the_ledger_and_is_paid_by_voucher()
    {
        var o = await fx.OwnerAsync();
        var clerk = await UserAsync(o, "ACCOUNTANT", "EXPENSE_RECORD");
        var cats = await o.GetJsonAsync("/api/v1/expenses/categories");
        var rent = cats.EnumerateArray().First(c => c.GetProperty("code").GetString() == "RENT").GetProperty("id").GetInt64();

        var e = await clerk.PostAsync("/api/v1/expenses", new { categoryId = rent, amount = 30000, description = "November rent", payeeId = (long?)null, submit = true });
        Assert.Equal(HttpStatusCode.Created, e.Status);
        var id = e.Body.GetProperty("id").GetInt64();
        Assert.Equal("SUBMITTED", e.Body.GetProperty("status").GetString());

        // the submitter cannot approve (even with the right), and a clerk without the right cannot either
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync($"/api/v1/expenses/{id}/approve")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/v1/expenses/{id}/approve")).Status);
        var posted = await o.GetJsonAsync($"/api/v1/expenses/{id}");
        Assert.Equal("POSTED", posted.GetProperty("status").GetString());
        await AssertEntryBalanced(posted.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.PostAsync($"/api/v1/expenses/{id}/approve")).Status);   // once

        // pay in two parts; more than owed is refused; a posted expense cannot be edited
        var p1 = await o.PostAsync($"/api/v1/expenses/{id}/pay", new { amount = 10000, mode = "CASH" });
        Assert.Equal(HttpStatusCode.OK, p1.Status);
        Assert.Equal("PARTIAL", p1.Body.GetProperty("expense").GetProperty("paymentStatus").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.PostAsync($"/api/v1/expenses/{id}/pay", new { amount = 25000, mode = "CASH" })).Status);
        var p2 = await o.PostAsync($"/api/v1/expenses/{id}/pay", new { amount = 20000, mode = "BANK" });
        Assert.Equal("PAID", p2.Body.GetProperty("expense").GetProperty("paymentStatus").GetString());
        Assert.Equal(30000, p2.Body.GetProperty("expense").GetProperty("paidAmount").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.PutAsync($"/api/v1/expenses/{id}", new { categoryId = rent, amount = 1 })).Status);

        // a rejected expense carries its reason and can be corrected
        var e2 = (await clerk.PostAsync("/api/v1/expenses", new { categoryId = rent, amount = 500, submit = true })).Body.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync($"/api/v1/expenses/{e2}/reject", new { reason = " " })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/v1/expenses/{e2}/reject", new { reason = "no bill" })).Status);
        Assert.Equal("no bill", (await o.GetJsonAsync($"/api/v1/expenses/{e2}")).GetProperty("rejectedReason").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await clerk.PutAsync($"/api/v1/expenses/{e2}", new { categoryId = rent, amount = 600, description = "with bill", submit = true })).Status);
    }

    [Fact]
    public async Task Head_office_expense_can_be_split_between_stores_and_the_shares_must_add_up()
    {
        var o = await fx.OwnerAsync();
        var second = await Id(o, "/api/v1/stores", new { code = U("S").ToUpperInvariant(), name = "Branch" });
        var util = (await o.GetJsonAsync("/api/v1/expenses/categories")).EnumerateArray().First(c => c.GetProperty("code").GetString() == "UTILITY").GetProperty("id").GetInt64();
        var bad = await o.PostAsync("/api/v1/expenses", new { storeId = 0, categoryId = util, amount = 1000, allocations = new[] { new { storeId = fx.Acme.FirstStoreId, amount = 400 }, new { storeId = second, amount = 500 } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        var ok = await o.PostAsync("/api/v1/expenses", new { storeId = 0, categoryId = util, amount = 1000, submit = true, allocations = new[] { new { storeId = fx.Acme.FirstStoreId, amount = 400 }, new { storeId = second, amount = 600 } } });
        Assert.Equal(HttpStatusCode.Created, ok.Status);
        Assert.Equal(2, ok.Body.GetProperty("allocations").GetArrayLength());
        // a store expense cannot be split
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/expenses", new { storeId = fx.Acme.FirstStoreId, categoryId = util, amount = 100, allocations = new[] { new { storeId = second, amount = 100 } } })).Status);
    }

    [Fact]
    public async Task Commission_accrues_per_sale_is_approved_flows_into_the_salary_sheet_and_salary_is_paid_in_parts()
    {
        var s = await NewShopAsync();
        await BuyAsync(s);
        var emp = await Id(s.Owner, "/api/v1/parties", new { name = U("Salesman"), isStaff = true, staff = new { designation = "Salesman", baseSalary = 30000, storeId = fx.Acme.FirstStoreId } });
        var plan = await Id(s.Owner, "/api/v1/hr/commission-plans", new { name = U("2 percent"), appliesTo = "SALESMAN", basis = "NET_SALE", calcType = "PERCENT", defaultRate = 2, minStoreSales = 0, validFrom = "2026-01-01", members = new[] { emp } });
        Assert.True(plan > 0);

        // sale of 2 x 2000 by the salesman -> 2% of 4000 = 80, pending
        var sale = (await s.Owner.PostAsync("/api/v1/sales", new { salesmanId = emp, lines = new[] { new { barcode = s.Barcode, qty = 2 } }, payInFull = true })).Body.GetProperty("invoice");
        var entries = await s.Owner.GetJsonAsync($"/api/v1/hr/commissions?employeeId={emp}&storeId={fx.Acme.FirstStoreId}");
        Assert.Equal(1, entries.GetProperty("total").GetInt32());
        var en = entries.GetProperty("items")[0];
        Assert.Equal(80, en.GetProperty("commissionAmount").GetInt64());
        Assert.Equal("PENDING", en.GetProperty("status").GetString());

        // Returning the whole line reverses the whole commission (reversal rows carry negative amounts).
        // KNOWN v6 DEFECT, deliberately not tested as correct: returning only PART of a line also reverses the WHOLE commission of that line
        // (hr.accrue_line_commission, and ux_comm_reverse_once allows one reversal per earn row). See docs/V6-SCHEMA-FINDINGS.md.
        var ret = await s.Owner.PostAsync("/api/v1/sales/returns", new { originalInvoiceId = sale.GetProperty("id").GetInt64(), lines = new[] { new { originalLineId = sale.GetProperty("lines")[0].GetProperty("id").GetInt64(), qty = 2 } } });
        Assert.Equal(HttpStatusCode.Created, ret.Status);
        long Net() => s.Owner.GetJsonAsync($"/api/v1/hr/commissions?employeeId={emp}").GetAwaiter().GetResult().GetProperty("items").EnumerateArray().Sum(x => x.GetProperty("commissionAmount").GetInt64());
        Assert.Equal(0, Net());
        await s.Owner.PostAsync("/api/v1/sales", new { salesmanId = emp, lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true });   // a later sale of one piece earns 2% of 2000
        Assert.Equal(40, Net());

        // approve this month's commission for the store, build and approve the sheet
        var from = DateTime.UtcNow.ToString("yyyy-MM-01"); var to = Today;
        var ap = await s.Owner.PostAsync("/api/v1/hr/commissions/approve", new { storeId = fx.Acme.FirstStoreId, from, to });
        Assert.Equal(HttpStatusCode.OK, ap.Status);
        Assert.True(ap.Body.GetProperty("approved").GetInt32() >= 1);
        await s.Owner.PostAsync("/api/v1/hr/payroll/adjustments", new { employeeId = emp, month = from, type = "DEDUCTION", amount = 1000, reason = "advance" });
        var run = await s.Owner.PostAsync("/api/v1/hr/payroll/build", new { storeId = fx.Acme.FirstStoreId, month = from });
        Assert.Equal(HttpStatusCode.OK, run.Status);
        var line = run.Body.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("employeePartyId").GetInt64() == emp);
        Assert.Equal(30000, line.GetProperty("baseSalary").GetInt64());
        Assert.Equal(1000, line.GetProperty("deductionAmount").GetInt64());
        var commission = line.GetProperty("commissionAmount").GetInt64();
        Assert.Equal(30000 + commission - 1000, line.GetProperty("netPay").GetInt64());

        var approved = await s.Owner.PostAsync($"/api/v1/hr/payroll/{run.Body.GetProperty("id").GetInt64()}/approve");
        Assert.Equal(HttpStatusCode.OK, approved.Status);
        Assert.Equal("APPROVED", approved.Body.GetProperty("status").GetString());
        await AssertEntryBalanced(approved.Body.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/hr/payroll/{run.Body.GetProperty("id").GetInt64()}/approve")).Status);   // once

        var lineId = line.GetProperty("id").GetInt64(); var net = line.GetProperty("netPay").GetInt64();
        var p1 = await s.Owner.PostAsync($"/api/v1/hr/payroll/lines/{lineId}/pay", new { amount = 10000, method = "CASH" });
        Assert.Equal(HttpStatusCode.OK, p1.Status);
        Assert.Equal(net - 10000, p1.Body.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/hr/payroll/lines/{lineId}/pay", new { amount = net, method = "CASH" })).Status);   // more than is left
        var p2 = await s.Owner.PostAsync($"/api/v1/hr/payroll/lines/{lineId}/pay", new { amount = net - 10000, method = "BANK" });
        Assert.Equal("PAID", p2.Body.GetProperty("status").GetString());
        Assert.Equal(0, p2.Body.GetProperty("outstandingAmount").GetInt64());
    }

    [Fact]
    public async Task Payroll_and_commission_need_their_own_rights()
    {
        var o = await fx.OwnerAsync();
        var plain = await UserAsync(o, "CASHIER_SALESMAN", "SALE_CREATE");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.SendAsync(HttpMethod.Get, "/api/v1/hr/commissions")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync("/api/v1/hr/payroll/build", new { storeId = fx.Acme.FirstStoreId, month = "2026-11-01" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync("/api/v1/hr/commission-plans", new { name = "x", appliesTo = "SALESMAN", basis = "NET_SALE", calcType = "PERCENT", defaultRate = 5, validFrom = "2026-01-01" })).Status);
        var preparer = await UserAsync(o, "ACCOUNTANT", "PAYROLL_PREPARE");
        var build = await preparer.PostAsync("/api/v1/hr/payroll/build", new { storeId = fx.Acme.FirstStoreId, month = "2026-10-01" });
        Assert.Equal(HttpStatusCode.OK, build.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsync($"/api/v1/hr/payroll/{build.Body.GetProperty("id").GetInt64()}/approve")).Status);   // preparing is not approving
        Assert.Equal(HttpStatusCode.NoContent, (await preparer.PostAsync($"/api/v1/hr/payroll/{build.Body.GetProperty("id").GetInt64()}/cancel")).Status);
    }

    [Fact]
    public async Task Cash_count_compares_denominations_to_what_the_books_expect_and_promises_track_payments()
    {
        var s = await NewShopAsync();
        await BuyAsync(s);
        await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true });   // +2000 cash
        var exp = await s.Owner.GetJsonAsync($"/api/v1/cash/expected?storeId={fx.Acme.FirstStoreId}");
        var expected = exp.GetProperty("expectedCash").GetInt64();   // cumulative over the whole shared test database, so only differences are asserted


        var count = await s.Owner.PostAsync("/api/v1/cash/counts", new { storeId = fx.Acme.FirstStoreId, countedCash = 1750, denominations = new Dictionary<string, int> { ["1000"] = 1, ["500"] = 1, ["100"] = 2, ["50"] = 1 }, note = "short by a bit" });
        Assert.Equal(HttpStatusCode.Created, count.Status);
        Assert.Equal(1750, count.Body.GetProperty("countedCash").GetInt64());
        Assert.Equal(1750 - expected, count.Body.GetProperty("difference").GetInt64());
        Assert.Equal(4, count.Body.GetProperty("denominations").GetArrayLength());
        // one count per store per day; denominations must add up; values must be real
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.PostAsync("/api/v1/cash/counts", new { storeId = fx.Acme.FirstStoreId, countedCash = 1 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/cash/counts", new { storeId = fx.Acme.FirstStoreId, date = "2026-01-05", countedCash = 999, denominations = new Dictionary<string, int> { ["500"] = 1 } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/cash/counts", new { storeId = fx.Acme.FirstStoreId, date = "2026-01-06", countedCash = 30, denominations = new Dictionary<string, int> { ["30"] = 1 } })).Status);

        var due = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
        var pr = await Id(s.Owner, "/api/v1/cash/promises", new { customerId = s.Customer, dueDate = due, amount = 1000, note = "Friday" });
        var half = await s.Owner.PostAsync($"/api/v1/cash/promises/{pr}/payment", new { amount = 400 });
        Assert.Equal("PARTIAL", half.Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/cash/promises/{pr}/payment", new { amount = 700 })).Status);
        Assert.Equal("PAID", (await s.Owner.PostAsync($"/api/v1/cash/promises/{pr}/payment", new { amount = 600 })).Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/cash/promises", new { customerId = s.Customer, dueDate = "2020-01-01", amount = 5 })).Status);
    }

    [Fact]
    public async Task Reports_balance_follow_the_users_rights_and_stay_inside_the_company()
    {
        var s = await NewShopAsync();
        await BuyAsync(s);
        await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 2 } }, payInFull = true });
        var from = DateTime.UtcNow.ToString("yyyy-MM-01");

        var tb = await s.Owner.GetJsonAsync($"/api/v1/reports/trial-balance?from={from}&to={Today}");
        var rows = tb.GetProperty("rows").EnumerateArray().ToList();
        Assert.True(rows.Count > 3);
        Assert.Equal(rows.Sum(r => r.GetProperty("periodDebit").GetInt64()), rows.Sum(r => r.GetProperty("periodCredit").GetInt64()));   // the books balance
        Assert.Equal(rows.Sum(r => r.GetProperty("closing").GetInt64()), 0);

        var salesAcc = (await s.Owner.GetJsonAsync("/api/v1/accounting/accounts")).EnumerateArray().First(a => a.GetProperty("systemRole").GetString() == "SALES" && a.GetProperty("storeId").GetInt64() == 0).GetProperty("id").GetInt64();
        var ledger = await s.Owner.GetJsonAsync($"/api/v1/reports/account-ledger?accountId={salesAcc}&from={from}&to={Today}");
        Assert.True(ledger.GetProperty("rows").GetArrayLength() >= 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.SendAsync(HttpMethod.Get, "/api/v1/reports/account-ledger")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.SendAsync(HttpMethod.Get, "/api/v1/reports/nonsense")).Status);

        var reg = await s.Owner.GetJsonAsync($"/api/v1/reports/sale-register?from={from}&to={Today}");
        Assert.True(reg.GetProperty("rows").GetArrayLength() >= 1);
        var profit = await s.Owner.GetJsonAsync($"/api/v1/reports/profit-summary?from={from}&to={Today}");
        Assert.True(profit.GetProperty("rows").GetArrayLength() >= 1);

        // a cashier sees no reports at all; a viewer sees operational ones but not the accounting ones
        var cashier = await UserAsync(s.Owner, "CASHIER_SALESMAN", "SALE_CREATE");
        Assert.Equal(0, (await cashier.GetJsonAsync("/api/v1/reports")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(HttpMethod.Get, $"/api/v1/reports/sale-register?from={from}&to={Today}")).Status);
        var viewer = await UserAsync(s.Owner, "ACCOUNTANT", "REPORT_VIEW");
        var visible = (await viewer.GetJsonAsync("/api/v1/reports")).EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToList();
        Assert.Contains("sale-register", visible); Assert.DoesNotContain("trial-balance", visible);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(HttpMethod.Get, $"/api/v1/reports/trial-balance?from={from}&to={Today}")).Status);

        // another company's trial balance does not contain this company's sale
        var g = await fx.LoginAsync("Globex Shoes", "owner", ApiFixture.OwnerPassword);
        var gtb = (await g.GetJsonAsync($"/api/v1/reports/trial-balance?from={from}&to={Today}")).GetProperty("rows").EnumerateArray().Sum(r => r.GetProperty("periodDebit").GetInt64());
        Assert.Equal(0, gtb);
    }

    [Fact]
    public async Task Manual_journal_between_two_accounts_balances_and_refuses_closed_inputs()
    {
        var o = await fx.OwnerAsync();
        var accs = (await o.GetJsonAsync("/api/v1/accounting/accounts")).EnumerateArray().ToList();
        long A(string role) => accs.First(a => a.GetProperty("systemRole").GetString() == role && a.GetProperty("storeId").GetInt64() == 0).GetProperty("id").GetInt64();
        var j = await o.PostAsync("/api/v1/accounting/vouchers", new { type = "JOURNAL", amount = 700, debitAccountId = A("CASH"), creditAccountId = A("CAPITAL"), description = "owner capital" });
        Assert.Equal(HttpStatusCode.Created, j.Status);
        await AssertEntryBalanced(j.Body.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/accounting/vouchers", new { type = "JOURNAL", amount = 5, debitAccountId = A("CASH"), creditAccountId = A("CASH") })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/accounting/vouchers", new { type = "JOURNAL", amount = 5, debitAccountId = A("PARTY_CONTROL"), creditAccountId = A("CASH") })).Status);   // party control needs a party
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/accounting/vouchers", new { type = "EXPENSE", amount = 5, mode = "CASH", expenseAccountId = A("CASH") })).Status);   // not an expense account
        var rent = accs.First(a => a.GetProperty("code").GetString() == "EXP-RENT").GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Created, (await o.PostAsync("/api/v1/accounting/vouchers", new { type = "EXPENSE", amount = 250, mode = "CASH", expenseAccountId = rent, description = "tea" })).Status);
    }
}
