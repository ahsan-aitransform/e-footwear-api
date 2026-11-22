namespace EFootwear.BLL.Platform.Contracts;

public sealed record RestoreDrillResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Outcome,
    decimal? MeasuredRtoMinutes,
    decimal? MeasuredRpoMinutes,
    long? RowsChecked);
