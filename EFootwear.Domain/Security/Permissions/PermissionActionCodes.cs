namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// The actions a permission can name. Part 6 of PROJECT_REQUIREMENTS: view, add, edit, delete, print, export, approve,
/// change selling price, give discount. <see cref="RestrictedOperation"/> is used only by the legacy codes that the
/// SQL functions check through sec.require_permission (SALE_CREATE, PRICE_CHANGE, ...).
/// </summary>
public static class PermissionActionCodes
{
    public const string View = "VIEW";
    public const string Add = "ADD";
    public const string Edit = "EDIT";
    public const string Delete = "DELETE";
    public const string Print = "PRINT";
    public const string Export = "EXPORT";
    public const string Approve = "APPROVE";
    public const string ChangeSellingPrice = "CHANGE_SELLING_PRICE";
    public const string GiveDiscount = "GIVE_DISCOUNT";

    /// <summary>The text shown for an action in the permission tree.</summary>
    public static string DisplayNameOf(string actionCode) => actionCode switch
    {
        View => "View",
        Add => "Add",
        Edit => "Edit",
        Delete => "Delete",
        Print => "Print",
        Export => "Export",
        Approve => "Approve",
        ChangeSellingPrice => "Change selling price",
        GiveDiscount => "Give discount",
        _ => actionCode,
    };
}
