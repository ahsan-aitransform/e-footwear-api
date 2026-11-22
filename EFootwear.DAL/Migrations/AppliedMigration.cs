namespace EFootwear.DAL.Migrations;

/// <summary>A row of ops.schema_migrations: a migration that has been applied to the database.</summary>
public sealed record AppliedMigration(string Version, string Name, string Checksum, DateTimeOffset AppliedAt);
