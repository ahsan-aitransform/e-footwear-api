namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>How many notes of one denomination were counted (docx 3.6: 5000, 1000, 500, 100, 50, 20, 10, 5, 2, 1). Used in requests and responses.</summary>
public sealed record CashCountDenominationLine(int Denomination, int NoteCount);
