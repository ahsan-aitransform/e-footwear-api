namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Change Product Attributes, docx 4.6: every product whose field (see ProductAttributeFieldCodes) has the old value gets the new value. Sent for the preview and for the update.</summary>
public sealed record ChangeProductAttributeRequest(
    string FieldType,
    Guid OldValueId,
    Guid NewValueId,
    Guid? ClientTransactionId);
