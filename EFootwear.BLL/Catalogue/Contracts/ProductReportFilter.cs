namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Product Report (docx 4.15) filters: company (brand), season and the colour improvement. All optional.</summary>
public sealed record ProductReportFilter(Guid? BrandId = null, Guid? SeasonId = null, Guid? ColorId = null);
