using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Payroll.Abstractions;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Payroll;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Payroll.Services;

/// <summary>
/// The monthly salary sheet of a store (docx 3.8 New Salary Sheet, 3.9 View Salary Sheet; Part 16.3). The payroll rules live in the database:
/// <c>hr.build_payroll_run</c> builds or rebuilds a DRAFT sheet (base salary in force at month end, approved commission, incentives and deductions),
/// <c>hr.approve_payroll_run</c> approves it and posts the salary expense, <c>hr.pay_salary_once</c> pays an approved line. This service decides who may
/// do what, checks the request and keeps the order of the steps. A sheet is never paid automatically: paying is a separate action per line.
///
/// The docx "Edit" and "Delete" of a saved sheet conflict with immutable posted entries: a draft is replaced by building it again, an approved sheet is final
/// (corrections are entered as adjustments of a later month).
/// </summary>
public sealed class PayrollSheetService(
    IPayrollRepository payrollRepository,
    IPartyRepository partyRepository,
    StoreAccessGuard storeAccessGuard,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PayrollSheetService> logger)
{
    public async Task<PayrollSheetResponse?> GetSheetAsync(Guid storeId, DateOnly month, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return await payrollRepository.FindSheetAsync(storeId, PayrollRules.NormalizeToFirstDayOfMonth(month), cancellationToken);
    }

    public async Task<PayrollSheetResponse> PrepareSheetAsync(PrepareSalarySheetRequest request, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(request.StoreId, cancellationToken);
        var periodMonth = PayrollRules.NormalizeToFirstDayOfMonth(request.Month);

        var runId = await payrollRepository.BuildSheetAsync(request.StoreId, periodMonth, currentUser.UserId, cancellationToken);
        var sheet = await payrollRepository.FindSheetByRunIdAsync(runId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The salary sheet could not be read after it was built.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Salary sheet {RunId} of store {StoreId} for {PeriodMonth} prepared by user {UserId} with {LineCount} lines",
            runId, request.StoreId, periodMonth, currentUser.UserId, sheet.Lines.Count);
        return sheet;
    }

    public async Task<PayrollSheetResponse> ApproveSheetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var sheet = await payrollRepository.FindSheetByRunIdAsync(runId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The salary sheet was not found.");
        if (!currentUser.CanAccessStore(sheet.StoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        EnsureMayPostToHeadOffice();
        if (sheet.StatusCode != PayrollRunStatusCodes.Draft)
            throw new DomainException(ErrorKind.Conflict, "Only a draft salary sheet can be approved. This one is already approved.");
        if (sheet.Lines.Count == 0)
            throw new DomainException(ErrorKind.Validation, "The salary sheet has no staff. Add staff to the store and build the sheet again.");

        var journalEntryId = await payrollRepository.ApproveSheetAsync(runId, currentUser.UserId, cancellationToken);
        var approvedSheet = await payrollRepository.FindSheetByRunIdAsync(runId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The salary sheet could not be read after it was approved.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Salary sheet {RunId} approved by user {UserId}; salary expense posted in journal entry {JournalEntryId}",
            runId, currentUser.UserId, journalEntryId);
        return approvedSheet;
    }

    public async Task<IReadOnlyList<PayrollAdjustmentResponse>> ListAdjustmentsAsync(Guid storeId, DateOnly month, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return await payrollRepository.ListAdjustmentsAsync(storeId, PayrollRules.NormalizeToFirstDayOfMonth(month), cancellationToken);
    }

    public async Task<CreatedIdResponse> CreateAdjustmentAsync(CreatePayrollAdjustmentRequest request, CancellationToken cancellationToken)
    {
        PayrollRules.EnsureAdjustmentIsValid(request.AdjustmentTypeCode, request.Amount, request.Reason);
        var periodMonth = PayrollRules.NormalizeToFirstDayOfMonth(request.Month);

        var staffStoreId = await partyRepository.FindStaffStoreIdAsync(request.EmployeePartyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.Validation, "Choose a staff member.");
        if (!currentUser.CanAccessStore(staffStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to the store of this staff member.");
        if (await payrollRepository.HasApprovedLineAsync(request.EmployeePartyId, periodMonth, cancellationToken))
            throw new DomainException(ErrorKind.Conflict,
                "The salary of this person for that month is already approved, so a new adjustment would never be paid. Enter it for the next month.");

        var cleanedRequest = request with { Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim() };
        var adjustmentId = await payrollRepository.AddAdjustmentAsync(cleanedRequest, periodMonth, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Payroll adjustment {AdjustmentId} ({AdjustmentTypeCode}) entered for staff {EmployeePartyId} by user {UserId}",
            adjustmentId, request.AdjustmentTypeCode, request.EmployeePartyId, currentUser.UserId);
        return new CreatedIdResponse(adjustmentId);
    }

    public async Task<SalaryPaymentResponse> PaySalaryAsync(Guid lineId, PaySalaryRequest request, CancellationToken cancellationToken)
    {
        PayrollRules.EnsurePaymentIsValid(request.Amount, request.MethodCode, request.Reference);

        // A retry is answered with the first payment before anything that depends on what is still owed is checked.
        if (request.ClientTransactionId is { } clientTransactionId
            && await payrollRepository.FindPaymentByClientTransactionAsync(clientTransactionId, cancellationToken) is { } firstPayment)
            return firstPayment;

        var line = await payrollRepository.FindLineAsync(lineId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The salary line was not found.");
        if (!currentUser.CanAccessStore(line.StoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        EnsureMayPostToHeadOffice();
        EnsureLineCanBePaid(line, request.Amount);

        var paymentId = await payrollRepository.PaySalaryAsync(
            new SalaryPaymentWriteRecord(lineId, request.Amount, request.MethodCode, request.PaidOn, NormalizeReference(request.Reference),
                                         request.ClientTransactionId, currentUser.UserId),
            cancellationToken);
        var payment = await payrollRepository.FindPaymentAsync(paymentId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The payment could not be read after it was recorded.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Salary payment {PaymentId} of {Amount} recorded for line {LineId} by user {UserId}",
            paymentId, request.Amount, lineId, currentUser.UserId);
        return payment;
    }

    public async Task<IReadOnlyList<SalaryPaymentResponse>> ListLinePaymentsAsync(Guid lineId, CancellationToken cancellationToken)
    {
        var line = await payrollRepository.FindLineAsync(lineId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The salary line was not found.");
        if (!currentUser.CanAccessStore(line.StoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        return await payrollRepository.ListPaymentsOfLineAsync(lineId, cancellationToken);
    }

    public async Task<PagedResult<SalaryPaymentResponse>> SearchPaymentsAsync(
        SalaryPaymentSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.FromDate is { } fromDate && filter.ToDate is { } toDate && fromDate > toDate)
            throw new DomainException(ErrorKind.Validation, "The first date must not be after the last date.");
        if (filter.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return await payrollRepository.SearchPaymentsAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    /// <summary>
    /// hr.approve_payroll_run and hr.pay_salary post the salary payable to the head-office account (store 0), and row-level security lets only people who
    /// may work in every store post there. A store-limited user would get a bare database error, so the reason is told plainly here.
    /// [BUSINESS DECISION REQUIRED] who may approve and pay salary (for example a head-office accountant role with all-store access).
    /// </summary>
    private void EnsureMayPostToHeadOffice()
    {
        if (!currentUser.HasAccessToAllStores)
            throw new DomainException(ErrorKind.Forbidden,
                "Approving a salary sheet and paying salary are posted to the head office, so only people who may work in every store can do it.");
    }

    private static void EnsureLineCanBePaid(PayrollLineSummary line, long amount)
    {
        switch (line.StatusCode)
        {
            case PayrollLineStatusCodes.Approved:
                break;
            case PayrollLineStatusCodes.Draft:
                throw new DomainException(ErrorKind.Conflict, "Salary can be paid only after the salary sheet is approved.");
            case PayrollLineStatusCodes.Paid:
                throw new DomainException(ErrorKind.Conflict, "This salary is already paid in full.");
            default:
                throw new DomainException(ErrorKind.Conflict, "This salary line was cancelled and cannot be paid.");
        }
        if (amount > line.OutstandingAmount)
            throw new DomainException(ErrorKind.Validation, $"The payment is more than the outstanding salary of {line.OutstandingAmount}.");
    }

    private static string? NormalizeReference(string? reference) => string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
}
