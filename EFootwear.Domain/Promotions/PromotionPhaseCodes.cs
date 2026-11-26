namespace EFootwear.Domain.Promotions;

/// <summary>
/// Where a promotion stands today, worked out from its status and its dates when it is read (never stored): INACTIVE (switched off), SCHEDULED (switched on, not started),
/// RUNNING, ENDED. Prices follow the pricing engine, which is run when a promotion is saved and by "Apply schedule now"; nothing runs it at the minute a promotion starts
/// or ends, so the phase can be ahead of the prices until it is run again.
/// </summary>
public static class PromotionPhaseCodes
{
    public const string Inactive = "INACTIVE";
    public const string Scheduled = "SCHEDULED";
    public const string Running = "RUNNING";
    public const string Ended = "ENDED";
}
