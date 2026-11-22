using System.Runtime.CompilerServices;

namespace EFootwear.Tests.Infrastructure;

/// <summary>
/// Finds files of the repository (the schema, the migrations, the generated source). The location of this source file is
/// recorded at compile time, so it works wherever the build output goes (for example --artifacts-path).
/// </summary>
public static class RepositoryPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string BaselineSchemaFile => Path.Combine(RepositoryRoot, "db", "schema_v7_uuid.sql");
    public static string MigrationsDirectory => Path.Combine(RepositoryRoot, "db", "migrations");
    public static string GeneratedPermissionCodesFile =>
        Path.Combine(RepositoryRoot, "EFootwear.Domain", "Security", "Permissions", "PermissionCodes.generated.cs");

    private static string FindRepositoryRoot([CallerFilePath] string thisFilePath = "")
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(thisFilePath)!); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "E-Footwear-Api.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("Could not find the repository root (E-Footwear-Api.slnx).");
    }
}
