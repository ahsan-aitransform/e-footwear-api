namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>The checked and normalised product header that the service hands to the repository (text trimmed, references verified, ranges checked).</summary>
public sealed record ProductHeaderData(
    string Name,
    string? NameUrdu,
    Guid DepartmentId,
    Guid CategoryId,
    Guid? BrandId,
    Guid? SeasonId,
    Guid? GenderId,
    Guid? ProductTypeId,
    Guid? UnitId,
    Guid? LocationId,
    decimal? Weight,
    string? Description,
    decimal TaxRate,
    string? TaxCode,
    string Status);
