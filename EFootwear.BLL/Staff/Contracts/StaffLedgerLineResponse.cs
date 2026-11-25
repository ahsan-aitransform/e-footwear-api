namespace EFootwear.BLL.Staff.Contracts;

/// <summary>
/// One journal line of a staff member. SourceType says what posted it (SALARY_PAYMENT, VOUCHER, OPENING ...); DocumentNumber is the voucher or invoice
/// number when the line has one. RunningBalanceAmount is the staff member's balance after this line (debit minus credit, over the whole history).
/// </summary>
public sealed record StaffLedgerLineResponse(
    Guid LineId,
    Guid StaffId,
    string StaffCode,
    string StaffName,
    DateOnly EntryDate,
    string SourceType,
    long? DocumentNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount);
