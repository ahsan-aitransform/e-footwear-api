namespace EFootwear.BLL.Customers.Contracts;

/// <summary>The filters of Customer Account Detail: the customer, From and To. A null customer means all customers. A null date is open-ended.</summary>
public sealed record CustomerLedgerQuery(Guid? CustomerId = null, DateOnly? From = null, DateOnly? To = null);
