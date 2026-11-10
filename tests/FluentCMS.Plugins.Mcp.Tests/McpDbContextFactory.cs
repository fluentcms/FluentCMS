using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Mcp.Tests;

// Used only by `dotnet ef migrations add` for the MCP stream, which never opens these databases. The argument after `--`
// names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class McpDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<McpPlugin>>
{
    public PluginDbContext<McpPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<McpPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new McpModel());
}
