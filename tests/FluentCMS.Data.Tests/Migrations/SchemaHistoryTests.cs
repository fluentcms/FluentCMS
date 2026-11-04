using System.Collections.Concurrent;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

public sealed class SchemaHistoryTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["Database:MigrateOnStartup"] = "false" };

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private static Action<IServiceCollection> Capture(CapturedLogs logs) => services => services.AddSingleton<ILoggerProvider>(logs);

    private static async Task<List<SchemaHistoryRow>> RowsAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        return [.. (await context.Set<SchemaHistoryRow>().AsNoTracking().ToListAsync(Token)).OrderBy(row => row.AppliedAt)];
    }

    private static async Task StartAndStopAsync(IHost host)
    {
        using (host)
        {
            await host.StartAsync(Token);
            await host.StopAsync(Token);
        }
    }

    [Fact]
    public async Task ApplyingContentMigrationsWritesTheSnapshotAndItsHash()
    {
        var before = DateTimeOffset.UtcNow;
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));
        await host.StartAsync(Token);

        var row = (await RowsAsync(host)).ShouldHaveSingleItem();
        var expected = SchemaSnapshot.Of(ArticleSchemas.Titled);
        row.Hash.ShouldBe(expected.Hash);
        row.Snapshot.ShouldBe(expected.Json);
        SchemaSnapshot.HashOf(row.Snapshot).ShouldBe(row.Hash);
        row.AppliedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        row.FluentcmsVersion.ShouldNotBeNullOrWhiteSpace();
        row.FluentcmsVersion.ShouldNotContain('+');
        _database.Columns("cms_schema_history").ShouldBe(["applied_at", "fluentcms_version", "hash", "id", "snapshot"]);
    }

    [Fact]
    public async Task ARestartWithTheSameSchemaAddsNoRow()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations);
        await host.StartAsync(Token);

        (await RowsAsync(host)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ANewMigrationAddsANewestRow()
    {
        var first = GeneratedMigrations.Scaffold(ArticleSchemas.Titled, "Initial");
        var firstAssembly = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, first.SnapshotCode);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, firstAssembly));

        var second = GeneratedMigrations.Scaffold(ArticleSchemas.WithSubtitle, "AddSubtitle", firstAssembly);
        var secondAssembly = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, second.MigrationCode, second.MetadataCode, second.SnapshotCode);
        using var host = _database.CreateHost(ArticleSchemas.WithSubtitle, Production, secondAssembly);
        await host.StartAsync(Token);

        (await RowsAsync(host)).Select(row => row.Hash).ShouldBe([SchemaSnapshot.Of(ArticleSchemas.Titled).Hash, SchemaSnapshot.Of(ArticleSchemas.WithSubtitle).Hash]);
    }

    [Fact]
    public async Task AChangeTheDatabaseDoesNotStoreIsRecordedWhenStartupMigrates()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));

        using var host = _database.CreateHost(ArticleSchemas.TitledAndRequired, Production, migrations);
        await host.StartAsync(Token);

        (await RowsAsync(host)).Last().Hash.ShouldBe(SchemaSnapshot.Of(ArticleSchemas.TitledAndRequired).Hash);
    }

    [Fact]
    public async Task MigrateRecordsTheSchemaWhenStartupDoesNot()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));
        await host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);

        (await RowsAsync(host)).ShouldHaveSingleItem().Hash.ShouldBe(SchemaSnapshot.Of(ArticleSchemas.Titled).Hash);
    }

    [Fact]
    public async Task DevelopmentSyncRecordsTheSchemaOnceAndNoMore()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development);
        await host.StartAsync(Token);
        (await RowsAsync(host)).Count.ShouldBe(1);

        await host.Services.GetRequiredService<MigrationRunner>().SyncContentSchemaAsync(ArticleSchemas.Titled, Token);

        (await RowsAsync(host)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task StartupLogsTheDriftBetweenTheCodeAndTheLastAppliedSnapshot()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));
        var logs = new CapturedLogs();

        using var host = _database.CreateHost(ArticleSchemas.TitledAndRequired, Production, migrations, NoMigrationOnStartup, Capture(logs));
        await host.StartAsync(Token);

        var drift = logs.Warnings.ShouldHaveSingleItem();
        drift.ShouldContain("field 'article.title' changed (required)");
        drift.ShouldContain(SchemaSnapshot.Of(ArticleSchemas.Titled).Hash);
        drift.ShouldContain(SchemaSnapshot.Of(ArticleSchemas.TitledAndRequired).Hash);
        (await RowsAsync(host)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task NoDriftLogsNothing()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));
        var logs = new CapturedLogs();

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations, NoMigrationOnStartup, Capture(logs));
        await host.StartAsync(Token);

        logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnreadableStoredSnapshotIsReportedAndDoesNotStopStartup()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));
        _database.Execute("update cms_schema_history set hash = 'x', snapshot = 'not json'");
        var logs = new CapturedLogs();

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations, NoMigrationOnStartup, Capture(logs));
        await host.StartAsync(Token);

        logs.Warnings.ShouldHaveSingleItem().ShouldContain("the stored snapshot cannot be read");
    }

    [Fact]
    public async Task AnEmptyHistoryIsReportedAsInformation()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));
        _database.Execute("delete from cms_schema_history");
        var logs = new CapturedLogs();

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations, NoMigrationOnStartup, Capture(logs));
        await host.StartAsync(Token);

        logs.Warnings.ShouldBeEmpty();
        logs.Informations.ShouldContain(message => message.Contains("schema history is empty", StringComparison.Ordinal));
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _messages = new();

        public IReadOnlyList<string> Warnings => Of(LogLevel.Warning);

        public IReadOnlyList<string> Informations => Of(LogLevel.Information);

        public ILogger CreateLogger(string categoryName) => new Logger(_messages);

        public void Dispose()
        {
        }

        private List<string> Of(LogLevel level) => [.. _messages.Where(entry => entry.Level == level).Select(entry => entry.Message)];

        private sealed class Logger(ConcurrentQueue<(LogLevel, string)> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
