namespace EFootwear.Domain.Customers;

/// <summary>
/// What a customer owes, summed from the journal by a database trigger. Table: contacts.customer_balances. READ ONLY for the
/// application. Balance = DebitTotal - CreditTotal; positive means the customer owes the company.
/// </summary>
public sealed class CustomerBalance
{
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public long DebitTotal { get; set; }
    public long CreditTotal { get; set; }
    public long Balance { get; set; }
    public DateOnly? LastEntryDate { get; set; }
}
