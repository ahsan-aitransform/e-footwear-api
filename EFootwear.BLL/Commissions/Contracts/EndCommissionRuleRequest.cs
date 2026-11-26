namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>Ends a rule on the given last day. To change a rate, end the old rule and add a new one starting the day after.</summary>
public sealed record EndCommissionRuleRequest(DateOnly ValidTo);
