namespace E_Footwear_Api.Cli;

/// <summary>Finds a folder or file of the repository by walking up from the current directory (the commands run from anywhere inside it).</summary>
public static class RepositoryFolderLocator
{
    public static string? FindUpwards(string relativePath)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate) || File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
