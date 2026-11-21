namespace ApiUsers.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSql";

    /// <summary>Database engine: <see cref="SqlServer"/> (default) or <see cref="PostgreSql"/> (also covers Supabase).</summary>
    public string Provider { get; set; } = SqlServer;

    /// <summary>Applies migrations and seeds roles/admin when the app starts.</summary>
    public bool MigrateOnStartup { get; set; }
}
