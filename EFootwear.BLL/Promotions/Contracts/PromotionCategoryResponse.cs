namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>A category a sale covers, shown as "Department -- Category" (docx 2.16).</summary>
public sealed record PromotionCategoryResponse(Guid CategoryId, string CategoryName, string DepartmentName);
