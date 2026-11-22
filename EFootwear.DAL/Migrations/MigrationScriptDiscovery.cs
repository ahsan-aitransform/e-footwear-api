using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EFootwear.DAL.Migrations;

/// <summary>Finds migration scripts in a folder and pairs each up script with its down script.</summary>
public static partial class MigrationScriptDiscovery
{
    // 0001_permission_catalog_columns.sql / 0001_permission_catalog_columns.down.sql
    [GeneratedRegex(@"^(?<version>\d{4})_(?<name>[a-z0-9_]+?)(?<down>\.down)?\.sql$")]
    private static partial Regex ScriptFileNamePattern();

    public static IReadOnlyList<MigrationScript> Discover(string migrationsDirectory)
    {
        if (!Directory.Exists(migrationsDirectory))
            throw new DirectoryNotFoundException($"Migrations folder not found: {migrationsDirectory}");

        var upScripts = new SortedDictionary<string, (string Name, string Path)>(StringComparer.Ordinal);
        var downScripts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(migrationsDirectory, "*.sql"))
        {
            var match = ScriptFileNamePattern().Match(Path.GetFileName(path));
            if (!match.Success)
                throw new InvalidOperationException($"Migration file name is not NNNN_name.sql or NNNN_name.down.sql: {Path.GetFileName(path)}");

            var version = match.Groups["version"].Value;
            if (match.Groups["down"].Success)
            {
                if (!downScripts.TryAdd(version, path))
                    throw new InvalidOperationException($"Two down scripts for migration {version}.");
            }
            else if (!upScripts.TryAdd(version, (match.Groups["name"].Value, path)))
            {
                throw new InvalidOperationException($"Two migrations share the number {version}.");
            }
        }

        foreach (var downVersion in downScripts.Keys.Where(version => !upScripts.ContainsKey(version)))
            throw new InvalidOperationException($"Down script {downVersion} has no matching up script.");

        return upScripts
            .Select(script => new MigrationScript(script.Key, script.Value.Name, script.Value.Path,
                                                  downScripts.GetValueOrDefault(script.Key),
                                                  ComputeChecksum(script.Value.Path)))
            .ToList();
    }

    public static string ComputeChecksum(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).ReplaceLineEndings("\n"))));
}
