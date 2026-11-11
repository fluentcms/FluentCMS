using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.AiAssist.Tests;

// Used only by `dotnet ef migrations add` for the AI assist stream, which never opens these databases. The argument after
// `--` names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class AiAssistDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<AiAssistPlugin>>
{
    public PluginDbContext<AiAssistPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<AiAssistPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new AiUsageModel());
}
