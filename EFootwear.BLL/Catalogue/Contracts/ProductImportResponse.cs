namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// What an uploaded product file contains. Products are created only when there is no problem at all; CreatedProductCount is zero in a preview
/// and when the file has problems.
/// </summary>
public sealed record ProductImportResponse(
    IReadOnlyList<ProductImportProductResponse> Products,
    IReadOnlyList<ProductImportProblemResponse> Problems,
    int CreatedProductCount);

public sealed record ProductImportProductResponse(string Name, string DepartmentName, string CategoryName, int SizeCount);

/// <summary>RowNumber counts the rows of the file that are not empty, the heading row being 1.</summary>
public sealed record ProductImportProblemResponse(int RowNumber, string ProductName, string SizeCode, string Message);
