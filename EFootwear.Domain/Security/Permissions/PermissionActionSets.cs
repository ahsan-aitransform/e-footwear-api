namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// The usual combinations of actions. A screen gets only the actions that make sense for it, so the permission
/// tree does not fill up with options nobody can use.
/// </summary>
internal static class PermissionActionSets
{
    /// <summary>A data-entry screen with a list: create, change, remove, print, export.</summary>
    public static readonly string[] Entry =
    [
        PermissionActionCodes.View, PermissionActionCodes.Add, PermissionActionCodes.Edit,
        PermissionActionCodes.Delete, PermissionActionCodes.Print, PermissionActionCodes.Export,
    ];

    /// <summary>A data-entry screen whose records need a second person to approve them.</summary>
    public static readonly string[] EntryWithApproval = [.. Entry, PermissionActionCodes.Approve];

    /// <summary>A data-entry screen that may also set the company selling price: entry plus the restricted selling-price operation (New Product).</summary>
    public static readonly string[] EntryWithSellingPriceControl = [.. Entry, PermissionActionCodes.ChangeSellingPrice];

    /// <summary>A sales-style screen: entry plus the two restricted price operations.</summary>
    public static readonly string[] Sales =
    [
        .. Entry, PermissionActionCodes.ChangeSellingPrice, PermissionActionCodes.GiveDiscount,
    ];

    /// <summary>A list or search screen: open, change or remove existing rows, print, export.</summary>
    public static readonly string[] List =
    [
        PermissionActionCodes.View, PermissionActionCodes.Edit, PermissionActionCodes.Delete,
        PermissionActionCodes.Print, PermissionActionCodes.Export,
    ];

    /// <summary>A read-only report: look at it, print it, export it.</summary>
    public static readonly string[] Report =
    [
        PermissionActionCodes.View, PermissionActionCodes.Print, PermissionActionCodes.Export,
    ];

    /// <summary>A screen that only displays something.</summary>
    public static readonly string[] ViewOnly = [PermissionActionCodes.View];

    /// <summary>A screen that opens and then applies one change, for example a bulk price change.</summary>
    public static readonly string[] Operation = [PermissionActionCodes.View, PermissionActionCodes.Edit];

    /// <summary>A screen where a person looks at items and approves them.</summary>
    public static readonly string[] Approval = [PermissionActionCodes.View, PermissionActionCodes.Approve];

    /// <summary>A screen that is opened, filled in and saved, never deleted.</summary>
    public static readonly string[] Document =
    [
        PermissionActionCodes.View, PermissionActionCodes.Add, PermissionActionCodes.Edit,
        PermissionActionCodes.Print,
    ];
}
