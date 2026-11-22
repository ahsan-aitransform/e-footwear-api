using EFootwear.Domain.Security.Permissions;

namespace E_Footwear_Api.Cli;

/// <summary>
/// Developer command. Regenerates <c>PermissionCodes.generated.cs</c> from the permission catalog. Run it after adding or
/// renaming a screen in PermissionCatalogDefinitions. A unit test fails when the file is out of date.
///
///   dotnet run --project E-Footwear-Api -- generate-permission-codes [--output path]
/// </summary>
public static class GeneratePermissionCodesCommand
{
    public const string GeneratedFileRelativePath = "EFootwear.Domain/Security/Permissions/PermissionCodes.generated.cs";
    private const string OutputOption = "--output";

    public static int Run(string[] arguments)
    {
        var outputPath = arguments.Length >= 3 && arguments[1] == OutputOption
            ? arguments[2]
            : RepositoryFolderLocator.FindUpwards(GeneratedFileRelativePath);
        if (outputPath is null)
        {
            Console.Error.WriteLine($"Could not find {GeneratedFileRelativePath}. Run from inside the repository or pass {OutputOption} <path>.");
            return 1;
        }

        File.WriteAllText(outputPath, PermissionCodesSourceGenerator.GenerateSource(PermissionCatalog.Modules));
        Console.WriteLine($"Wrote {PermissionCatalog.AllEntries.Count} permission codes to {outputPath}");
        return 0;
    }
}
