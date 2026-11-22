namespace EFootwear.Domain.MasterData;

/// <summary>
/// Describes one master list: which table it lives in and how it is shown. The single source of truth for the ten generated
/// master tables; the controllers, the EF Core mapping and the usage lookup all read it.
/// </summary>
/// <param name="Key">Stable machine name of the list (also the URL segment of its controller).</param>
/// <param name="TableName">The table inside the master schema.</param>
/// <param name="DisplayName">The name used in messages (docx screen name).</param>
/// <param name="WarnAboutNearDuplicateNames">True when saving warns about names that look like a spelling slip of an existing name (cities, docx 2.17).</param>
public sealed record MasterListDefinition(string Key, string TableName, string DisplayName, bool WarnAboutNearDuplicateNames = false)
{
    public const string SchemaName = "master";

    /// <summary>Schema-qualified table, for example master.departments.</summary>
    public string QualifiedTableName => $"{SchemaName}.{TableName}";

    /// <summary>The name EF Core gives the shared-type entity of this list.</summary>
    public string EntityName => $"MasterList.{TableName}";
}
