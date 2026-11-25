namespace EFootwear.BLL.Contacts.Abstractions;

/// <summary>Allocates the next code of a customer, vendor or staff member (103 plus a serial). One counter serves the three, so a code is never used twice in a company.</summary>
public interface IContactCodeGenerator
{
    Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken);
}
