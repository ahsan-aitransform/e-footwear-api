using EFootwear.BLL.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>Checks shared by the inventory services. They give a short answer before a database function is called; the functions stay the authority.</summary>
internal static class InventoryRequestValidator
{
    public static void EnsureStoreAccess(ICurrentUser currentUser, Guid storeId)
    {
        if (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }

    public static void EnsureClientTransactionId(Guid clientTransactionId)
    {
        if (clientTransactionId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "The request has no transaction id. Reload the screen and try again.");
    }

    public static void EnsureDateIsGiven(DateOnly date, string fieldName)
    {
        if (date == default)
            throw new DomainException(ErrorKind.Validation, $"Choose {fieldName}.");
    }

    public static void EnsureDateRange(DateOnly? fromDate, DateOnly? toDate)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
            throw new DomainException(ErrorKind.Validation, "The From date must not be after the To date.");
    }

    public static void EnsureScanQuantity(int quantity, int maximum)
    {
        if (quantity < 1 || quantity > maximum)
            throw new DomainException(ErrorKind.Validation, $"Scan between 1 and {maximum} pieces at a time.");
    }

    public static void EnsureLineCount(int count, int maximum, string documentName)
    {
        if (count < 1)
            throw new DomainException(ErrorKind.Validation, $"Add at least one item to the {documentName}.");
        if (count > maximum)
            throw new DomainException(ErrorKind.Validation, $"A {documentName} can have at most {maximum} lines.");
    }

    public static void EnsureNoRepeatedSize(IEnumerable<Guid> variantIds)
    {
        if (variantIds.GroupBy(id => id).Any(group => group.Count() > 1))
            throw new DomainException(ErrorKind.Validation, "Each size can appear only once. Join the repeated lines.");
    }

    public static void EnsureVariantIsGiven(Guid variantId, int lineNumber)
    {
        if (variantId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: choose the article size.");
    }

    public static void EnsureUnitCostIsValid(long? unitCostAmount, int lineNumber)
    {
        if (unitCostAmount is < 0)
            throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: the purchase price cannot be negative.");
    }
}
