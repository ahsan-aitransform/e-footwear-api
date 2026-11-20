using EFootwear.Application.Abstractions;
using EFootwear.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFootwear.Api.Controllers;

[ApiController, Authorize, Route("api/v1/accounting")]
public sealed class AccountingController(AccountingService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("accounts")] public async Task<IActionResult> Accounts(bool includeInactive, CancellationToken ct) => Ok(await svc.AccountsAsync(me.User, includeInactive, ct));
    [HttpPost("accounts")] public async Task<IActionResult> CreateAccount(AccountBody body, CancellationToken ct) => Created("", new { id = await svc.CreateAccountAsync(me.User, body, ct) });
    [HttpPut("accounts/{id:long}")] public async Task<IActionResult> UpdateAccount(long id, AccountBody body, CancellationToken ct) { await svc.UpdateAccountAsync(me.User, id, body, ct); return NoContent(); }

    [HttpGet("parties/{id:long}/ledger")]
    public async Task<IActionResult> PartyLedger(long id, DateOnly? from, DateOnly? to, CancellationToken ct) => Ok(await svc.PartyLedgerAsync(me.User, id, from, to, ct));

    [HttpGet("vouchers")]
    public async Task<IActionResult> Vouchers([FromQuery] PageQuery q, long? storeId, string? type, DateOnly? from, DateOnly? to, long? partyId, string? status, CancellationToken ct)
        => Ok(await svc.VouchersAsync(me.User, new VoucherFilter(storeId, type, from, to, partyId, status), q, ct));

    [HttpGet("vouchers/{id:long}")] public async Task<IActionResult> Voucher(long id, CancellationToken ct) => Ok(await svc.VoucherAsync(me.User, id, ct));

    [HttpPost("vouchers")]
    public async Task<IActionResult> CreateVoucher(VoucherBody body, CancellationToken ct)
    {
        var v = await svc.CreateVoucherAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Voucher), new { id = v["id"] }, v);
    }

    [HttpPost("vouchers/{id:long}/void")] public async Task<IActionResult> VoidVoucher(long id, VoidReason body, CancellationToken ct) { await svc.VoidVoucherAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("opening-balances")] public async Task<IActionResult> Opening(OpeningBody body, CancellationToken ct) => Ok(await svc.OpeningAsync(me.User, body, ct));

    [HttpGet("vendors/summary")] public async Task<IActionResult> VendorSummary(CancellationToken ct) => Ok(await svc.VendorSummaryAsync(me.User, ct));
    [HttpGet("vendors/{id:long}/open-bills")] public async Task<IActionResult> OpenBills(long id, CancellationToken ct) => Ok(await svc.OpenBillsAsync(me.User, id, ct));
    [HttpGet("vendors/due")] public async Task<IActionResult> VendorDue(CancellationToken ct) => Ok(await svc.VendorDueAsync(me.User, ct));
    [HttpPost("vendors/planned-payments")] public async Task<IActionResult> Plan(SchedulePlanBody body, CancellationToken ct) => Created("", new { id = await svc.PlanPaymentAsync(me.User, body, ct) });
    [HttpDelete("vendors/planned-payments/{id:long}")] public async Task<IActionResult> CancelPlan(long id, CancellationToken ct) { await svc.CancelPlannedPaymentAsync(me.User, id, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/expenses")]
public sealed class ExpensesController(ExpenseService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("categories")] public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await svc.CategoriesAsync(me.User, ct));
    [HttpPost("categories")] public async Task<IActionResult> CreateCategory(ExpenseCategoryBody body, CancellationToken ct) => Created("", new { id = await svc.CreateCategoryAsync(me.User, body, ct) });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery q, long? storeId, string? status, string? paymentStatus, long? categoryId, DateOnly? from, DateOnly? to, CancellationToken ct)
        => Ok(await svc.ListAsync(me.User, new ExpenseFilter(storeId, status, paymentStatus, categoryId, from, to), q, ct));

    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(ExpenseBody body, CancellationToken ct)
    {
        var e = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id = e["id"] }, e);
    }

    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, ExpenseBody body, CancellationToken ct) { await svc.UpdateAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteDraftAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/submit")] public async Task<IActionResult> Submit(long id, CancellationToken ct) { await svc.SubmitAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/approve")] public async Task<IActionResult> Approve(long id, CancellationToken ct) { await svc.ApproveAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/reject")] public async Task<IActionResult> Reject(long id, RejectReason body, CancellationToken ct) { await svc.RejectAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("{id:long}/pay")] public async Task<IActionResult> Pay(long id, PayBody body, CancellationToken ct) => Ok(await svc.PayAsync(me.User, id, body, ct));
}

[ApiController, Authorize, Route("api/v1/hr")]
public sealed class HrController(HrService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("commission-plans")] public async Task<IActionResult> Plans(CancellationToken ct) => Ok(await svc.PlansAsync(me.User, ct));
    [HttpGet("commission-plans/{id:long}")] public async Task<IActionResult> Plan(long id, CancellationToken ct) => Ok(await svc.PlanAsync(me.User, id, ct));
    [HttpPost("commission-plans")] public async Task<IActionResult> CreatePlan(PlanBody body, CancellationToken ct) => Created("", new { id = await svc.CreatePlanAsync(me.User, body, ct) });
    [HttpPut("commission-plans/{id:long}")] public async Task<IActionResult> UpdatePlan(long id, PlanBody body, CancellationToken ct) { await svc.UpdatePlanAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("commission-plans/{id:long}/rules")] public async Task<IActionResult> AddRule(long id, RuleBody2 body, CancellationToken ct) => Created("", new { id = await svc.AddRuleAsync(me.User, id, body, ct) });
    [HttpPost("commission-rules/{id:long}/end")] public async Task<IActionResult> EndRule(long id, DateOnly validTo, CancellationToken ct) { await svc.EndRuleAsync(me.User, id, validTo, ct); return NoContent(); }

    [HttpGet("commissions")]
    public async Task<IActionResult> Entries([FromQuery] PageQuery q, long? storeId, long? employeeId, string? status, DateOnly? from, DateOnly? to, CancellationToken ct)
        => Ok(await svc.EntriesAsync(me.User, storeId, employeeId, status, from, to, q, ct));

    [HttpGet("commissions/summary")] public async Task<IActionResult> Summary(long? storeId, DateOnly? month, CancellationToken ct) => Ok(await svc.SummaryAsync(me.User, storeId, month, ct));
    [HttpPost("commissions/approve")] public async Task<IActionResult> Approve(ApproveCommissionsBody body, CancellationToken ct) => Ok(new { approved = await svc.ApproveCommissionsAsync(me.User, body, ct) });

    [HttpGet("payroll")] public async Task<IActionResult> Runs(long? storeId, CancellationToken ct) => Ok(await svc.RunsAsync(me.User, storeId, ct));
    [HttpGet("payroll/{id:long}")] public async Task<IActionResult> Run(long id, CancellationToken ct) => Ok(await svc.RunAsync(me.User, id, ct));
    [HttpPost("payroll/build")] public async Task<IActionResult> Build(BuildPayrollBody body, CancellationToken ct) => Ok(await svc.BuildPayrollAsync(me.User, body, ct));
    [HttpPost("payroll/{id:long}/approve")] public async Task<IActionResult> ApprovePayroll(long id, CancellationToken ct) => Ok(await svc.ApprovePayrollAsync(me.User, id, ct));
    [HttpPost("payroll/{id:long}/cancel")] public async Task<IActionResult> CancelPayroll(long id, CancellationToken ct) { await svc.CancelDraftPayrollAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("payroll/adjustments")] public async Task<IActionResult> Adjust(AdjustmentBody body, CancellationToken ct) => Created("", new { id = await svc.AddAdjustmentAsync(me.User, body, ct) });
    [HttpPost("payroll/lines/{id:long}/pay")] public async Task<IActionResult> Pay(long id, PaySalaryBody body, CancellationToken ct) => Ok(await svc.PaySalaryAsync(me.User, id, body, ct));
}

[ApiController, Authorize, Route("api/v1/cash")]
public sealed class CashController(CashService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("promises")] public async Task<IActionResult> Promises([FromQuery] PageQuery q, long? storeId, long? customerId, string? status, CancellationToken ct) => Ok(await svc.PromisesAsync(me.User, storeId, customerId, status, q, ct));
    [HttpPost("promises")] public async Task<IActionResult> CreatePromise(PromiseBody body, CancellationToken ct) => Created("", new { id = await svc.CreatePromiseAsync(me.User, body, ct) });
    [HttpPost("promises/{id:long}/payment")] public async Task<IActionResult> PromisePayment(long id, PromisePayment body, CancellationToken ct) => Ok(await svc.RecordPromisePaymentAsync(me.User, id, body, ct));
    [HttpPost("promises/{id:long}/cancel")] public async Task<IActionResult> CancelPromise(long id, CancellationToken ct) { await svc.CancelPromiseAsync(me.User, id, ct); return NoContent(); }

    [HttpGet("expected")] public async Task<IActionResult> Expected(long? storeId, DateOnly? date, CancellationToken ct) => Ok(await svc.ExpectedAsync(me.User, storeId, date, ct));
    [HttpGet("counts")] public async Task<IActionResult> Counts(long? storeId, DateOnly? from, DateOnly? to, CancellationToken ct) => Ok(await svc.CountsAsync(me.User, storeId, from, to, ct));
    [HttpPost("counts")] public async Task<IActionResult> Count(CashCountBody body, CancellationToken ct) => Created("", await svc.CountAsync(me.User, body, ct));
}

[ApiController, Authorize, Route("api/v1/reports")]
public sealed class ReportsController(ReportService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet] public IActionResult Catalog() => Ok(svc.For(me.User));

    [HttpGet("{key}")]
    public async Task<IActionResult> Run(string key, DateOnly? from, DateOnly? to, long? storeId, long? accountId, long? vendorId, long? customerId, long? categoryId, string? docType, CancellationToken ct)
        => Ok(await svc.RunAsync(me.User, key, from, to, storeId, accountId, vendorId, customerId, categoryId, docType, ct));
}
