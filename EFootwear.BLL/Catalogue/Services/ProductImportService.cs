using System.Globalization;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// New products from an Excel (.xlsx) or .csv file: the first row names the columns, every other row is one size, and rows with the same product
/// name are one product. Lists (department, category, brand ...) are named as they appear in Settings and are never created from a file. A product
/// name that already exists is refused, so uploading a file twice creates nothing twice. Either every product of the file is created, in one
/// transaction and by the same rules as the New Product screen, or none.
/// </summary>
public sealed class ProductImportService(
    ProductCatalogService productCatalogService,
    IProductRepository productRepository,
    IMasterListRepository masterListRepository,
    ICategoryRepository categoryRepository,
    IPricingEngineRepository pricingEngineRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductImportService> logger)
{
    public const int MaximumDataRows = 2000;
    public const string TemplateFileName = "new-products-template.xlsx";

    private const string Name = "Product Name", NameUrdu = "Product Name (Urdu)", Department = "Department", Category = "Category", Brand = "Brand",
        Season = "Season", Gender = "Gender", ProductType = "Product Type", Unit = "Unit", Location = "Location", Colour = "Colour",
        SizeGroup = "Size Group", Size = "Size", Barcode = "Barcode", PurchasePrice = "Purchase Price", SalePrice = "Sale Price", Description = "Description";

    private static readonly string[] Headings =
        [Name, NameUrdu, Department, Category, Brand, Season, Gender, ProductType, Unit, Location, Colour, SizeGroup, Size, Barcode, PurchasePrice, SalePrice, Description];
    private static readonly string[] RequiredHeadings = [Name, Department, Category, Size, PurchasePrice, SalePrice];
    /// <summary>The columns that describe the product; the others describe one size. A later row of a product may leave them empty.</summary>
    private static readonly string[] ProductHeadings = [NameUrdu, Department, Category, Brand, Season, Gender, ProductType, Unit, Location, Description];

    public static byte[] BuildTemplate() => SpreadsheetWriter.WriteWorkbook("Products", [Headings]);

    public async Task<ProductImportResponse> PreviewAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
        (await ReadAsync(content, fileName, cancellationToken)).ToResponse(createdProductCount: 0);

    public async Task<ProductImportResponse> ImportAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var file = await ReadAsync(content, fileName, cancellationToken);
        if (file.Problems.Count > 0) return file.ToResponse(createdProductCount: 0);

        foreach (var product in file.Products)
        {
            try
            {
                await productCatalogService.AddAsync(product.Request, cancellationToken);
            }
            catch (DomainException exception)
            {
                throw new DomainException(exception.Kind, $"{product.Request.Name}: {exception.Message}");
            }
        }
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{ProductCount} products imported from a file in company {CompanyId} by user {UserId}",
            file.Products.Count, currentUser.CompanyId, currentUser.UserId);
        return file.ToResponse(file.Products.Count);
    }

    private async Task<ImportFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var fileRows = SpreadsheetReader.ReadRows(content, fileName);
        if (fileRows.Count < 2)
            throw new DomainException(ErrorKind.Validation, "The file needs a heading row and at least one row of data.");
        if (fileRows.Count - 1 > MaximumDataRows)
            throw new DomainException(ErrorKind.Validation, $"The file can have at most {MaximumDataRows} rows of data.");
        var columns = ReadHeadings(fileRows[0]);
        var rows = fileRows.Skip(1).Select((cells, index) => new ImportRow(index + 2, columns, cells)).ToList();

        var problems = new List<ProductImportProblemResponse>();
        void Report(ImportRow row, string message) => problems.Add(new(row.Number, row[Name], row[Size], message));

        var departments = await ListIdsByNameAsync(MasterListDefinitions.Department, cancellationToken);
        var optionalLists = new Dictionary<string, IReadOnlyDictionary<string, Guid>>();
        foreach (var (heading, definition) in new[]
                 {
                     (Brand, MasterListDefinitions.Brand), (Season, MasterListDefinitions.Season), (Gender, MasterListDefinitions.Gender),
                     (ProductType, MasterListDefinitions.ProductType), (Unit, MasterListDefinitions.Unit), (Location, MasterListDefinitions.Location),
                     (Colour, MasterListDefinitions.Color), (SizeGroup, MasterListDefinitions.SizeGroup),
                 })
            optionalLists[heading] = await ListIdsByNameAsync(definition, cancellationToken);
        var categoriesByDepartment = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();

        Guid? Find(ImportRow row, string heading, string value)
        {
            if (value.Length == 0) return null;
            if (optionalLists[heading].TryGetValue(value, out var id)) return id;
            Report(row, $"{heading} \"{value}\" is not an active value in Settings.");
            return null;
        }

        var typedBarcodes = new Dictionary<string, ImportRow>(StringComparer.Ordinal);
        var products = new List<ImportProduct>();
        foreach (var group in rows.GroupBy(row => ProductRules.NormalizeName(row[Name]), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (group.Key.Length == 0)
            {
                foreach (var row in group) Report(row, "The product name is required.");
                continue;
            }

            var values = new Dictionary<string, string>();
            foreach (var heading in ProductHeadings)
            {
                var different = group.Select(row => row[heading]).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (different.Count > 1) Report(first, $"The rows of this product have different values for {heading}: {string.Join(", ", different)}.");
                values[heading] = different.Count > 0 ? different[0] : "";
            }

            Guid departmentId = default, categoryId = default;
            if (!departments.TryGetValue(values[Department], out departmentId))
                Report(first, values[Department].Length == 0 ? "The department is required." : $"Department \"{values[Department]}\" is not an active value in Settings.");
            else
            {
                if (!categoriesByDepartment.TryGetValue(departmentId, out var categories))
                    categoriesByDepartment[departmentId] = categories = ByName(await categoryRepository.ListActiveOptionsAsync(departmentId, cancellationToken));
                if (!categories.TryGetValue(values[Category], out categoryId))
                    Report(first, values[Category].Length == 0 ? "The category is required." : $"Category \"{values[Category]}\" is not an active category of department \"{values[Department]}\".");
            }

            var variants = new List<ProductVariantInput>();
            var seenSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group)
            {
                var sizeCode = VariantRules.NormalizeSizeCode(row[Size]);
                Check(row, () => VariantRules.EnsureValidSizeCode(sizeCode));
                var colorId = Find(row, Colour, row[Colour]);
                if (sizeCode.Length > 0 && !seenSizes.Add($"{row[Colour]}|{sizeCode}")) Report(row, "This size and colour is listed twice for the product.");

                var barcode = VariantRules.NormalizeBarcode(row[Barcode]);
                if (barcode.Length > 0)
                {
                    Check(row, () => VariantRules.EnsureValidBarcode(barcode));
                    if (!typedBarcodes.TryAdd(barcode, row)) Report(row, $"The barcode {barcode} appears more than once in the file.");
                }

                variants.Add(new ProductVariantInput(null, colorId, Find(row, SizeGroup, row[SizeGroup]), sizeCode, barcode.Length == 0 ? null : barcode,
                    ReadPrice(row, PurchasePrice), ReadPrice(row, SalePrice)));
            }
            if (variants.Count > VariantRules.MaximumVariantsPerProduct)
                Report(first, $"A product can have at most {VariantRules.MaximumVariantsPerProduct} sizes.");

            var nameUrdu = ProductRules.NormalizeOptionalText(values[NameUrdu]);
            var description = ProductRules.NormalizeDescription(values[Description]);
            Check(first, () => ProductRules.EnsureValidHeader(group.Key, nameUrdu, description, null, 0, null, ProductStatusCodes.Active));

            var request = new CreateProductRequest(group.Key, nameUrdu, departmentId, categoryId,
                Find(first, Brand, values[Brand]), Find(first, Season, values[Season]), Find(first, Gender, values[Gender]),
                Find(first, ProductType, values[ProductType]), Find(first, Unit, values[Unit]), Find(first, Location, values[Location]),
                null, description, 0, null, ProductStatusCodes.Active, variants);
            products.Add(new ImportProduct(first, values[Department], values[Category], request));
        }

        var existingNames = (await productRepository.FindNamesInUseAsync(products.Select(product => product.Request.Name).ToList(), cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products.Where(product => existingNames.Contains(product.Request.Name)))
            Report(product.FirstRow, "A product with this name already exists.");
        if (typedBarcodes.Count > 0)
            foreach (var barcode in await productRepository.FindBarcodesInUseAsync(typedBarcodes.Keys.ToList(), [], cancellationToken))
                Report(typedBarcodes[barcode], $"The barcode {barcode} is already used.");

        return new ImportFile(products, problems.OrderBy(problem => problem.RowNumber).ToList());

        void Check(ImportRow row, Action rule)
        {
            try { rule(); }
            catch (DomainException exception) { Report(row, exception.Message); }
        }

        long ReadPrice(ImportRow row, string heading)
        {
            if (decimal.TryParse(row[heading], NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                && price >= 0 && price <= CataloguePriceRules.MaximumPrice && price == decimal.Truncate(price))
                return (long)price;
            Report(row, $"{heading} must be a whole number of rupees.");
            return 0;
        }
    }

    private static Dictionary<string, int> ReadHeadings(IReadOnlyList<string> cells)
    {
        var columns = new Dictionary<string, int>();
        for (var index = 0; index < cells.Count; index++)
        {
            var text = cells[index].Trim().TrimEnd('*').Trim();
            if (text.Length == 0) continue;
            // an unknown heading is refused: ignoring a misspelled column would silently lose what was typed in it
            var heading = Headings.FirstOrDefault(known => known.Equals(text.Equals("Color", StringComparison.OrdinalIgnoreCase) ? Colour : text, StringComparison.OrdinalIgnoreCase))
                          ?? throw new DomainException(ErrorKind.Validation, $"The column \"{text}\" is not part of the template.");
            if (!columns.TryAdd(heading, index))
                throw new DomainException(ErrorKind.Validation, $"The column \"{heading}\" appears twice.");
        }
        var missing = RequiredHeadings.Where(heading => !columns.ContainsKey(heading)).ToList();
        if (missing.Count > 0)
            throw new DomainException(ErrorKind.Validation, $"The first row must have these columns: {string.Join(", ", missing)}.");
        return columns;
    }

    private async Task<IReadOnlyDictionary<string, Guid>> ListIdsByNameAsync(MasterListDefinition definition, CancellationToken cancellationToken) =>
        ByName(await masterListRepository.ListActiveOptionsAsync(definition, cancellationToken));

    private static Dictionary<string, Guid> ByName(IEnumerable<MasterData.Contracts.MasterListOptionResponse> options) =>
        options.GroupBy(option => option.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

    private sealed record ImportRow(int Number, IReadOnlyDictionary<string, int> Columns, IReadOnlyList<string> Cells)
    {
        public string this[string heading] => Columns.TryGetValue(heading, out var index) && index < Cells.Count ? Cells[index].Trim() : "";
    }

    private sealed record ImportProduct(ImportRow FirstRow, string DepartmentName, string CategoryName, CreateProductRequest Request);

    private sealed record ImportFile(IReadOnlyList<ImportProduct> Products, IReadOnlyList<ProductImportProblemResponse> Problems)
    {
        public ProductImportResponse ToResponse(int createdProductCount) => new(
            Products.Select(product => new ProductImportProductResponse(product.Request.Name, product.DepartmentName, product.CategoryName, product.Request.Variants.Count)).ToList(),
            Problems, createdProductCount);
    }
}
