namespace EFootwear.DAL.Migrations;

/// <summary>
/// One migration found in the migrations folder: <c>NNNN_name.sql</c> (up) and <c>NNNN_name.down.sql</c> (down).
/// <paramref name="Checksum"/> is the SHA-256 of the up script, stored when it is applied, so a script that is edited
/// afterwards is detected and refused.
/// </summary>
public sealed record MigrationScript(string Version, string Name, string UpScriptPath, string? DownScriptPath, string Checksum)
{
    public string UpScriptText => File.ReadAllText(UpScriptPath);

    public string DownScriptText => DownScriptPath is null
        ? throw new InvalidOperationException($"Migration {Version}_{Name} has no down script (expected {Version}_{Name}.down.sql).")
        : File.ReadAllText(DownScriptPath);
}
