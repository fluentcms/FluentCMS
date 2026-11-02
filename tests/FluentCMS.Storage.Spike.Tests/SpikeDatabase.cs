using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Storage.Spike.Tests;

internal sealed class SpikeDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SpikeDatabase() => _connection.Open();

    public SpikeDbContext CreateContext(
        SpikeSchema? schema = null,
        SpikeTables stream = SpikeTables.All,
        Assembly? migrations = null,
        string? historyTable = null)
    {
        var options = new DbContextOptionsBuilder<SpikeDbContext>()
            .UseSqlite(_connection, sqlite =>
            {
                if (migrations is not null)
                {
                    sqlite.MigrationsAssembly(migrations);
                }

                if (historyTable is not null)
                {
                    sqlite.MigrationsHistoryTable(historyTable);
                }
            })
            .ReplaceService<IModelCacheKeyFactory, SpikeModelCacheKeyFactory>()
            .Options;

        return new SpikeDbContext(options, schema ?? SpikeSchema.Article, stream);
    }

    public SpikeDbContext CreateMigratedContext()
    {
        var context = CreateContext();
        context.Database.EnsureCreated();
        return context;
    }

    public void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public List<string> ReadStrings(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
        }

        return values;
    }

    public static string QueryPlan(IQueryable query)
    {
        using var command = query.CreateDbCommand();
        command.CommandText = $"EXPLAIN QUERY PLAN {command.CommandText}";
        using var reader = command.ExecuteReader();

        var steps = new List<string>();
        while (reader.Read())
        {
            steps.Add(reader.GetString(3));
        }

        return string.Join('\n', steps);
    }

    public void Dispose() => _connection.Dispose();
}

internal static class SpikeSeed
{
    public static readonly Guid SiteA = Guid.CreateVersion7();
    public static readonly Guid SiteB = Guid.CreateVersion7();

    public static Guid AddShared(
        this SpikeDbContext context,
        Guid? documentId = null,
        int state = 1,
        Guid? siteId = null,
        string category = "news",
        string? sections = null,
        string? metadata = null,
        DateTimeOffset? publishedOn = null)
    {
        var id = documentId ?? Guid.CreateVersion7();
        context.Rows("cms_article").Add(new Dictionary<string, object>
        {
            ["document_id"] = id,
            ["state"] = state,
            ["site_id"] = siteId ?? SiteA,
            ["category"] = category,
            ["sections"] = sections!,
            ["metadata"] = metadata!,
            ["published_on"] = publishedOn!,
        });

        return id;
    }

    public static void AddLocale(
        this SpikeDbContext context,
        Guid documentId,
        string title,
        string slug,
        string locale = "en",
        int state = 1,
        Guid? siteId = null)
    {
        context.Rows("cms_article_i18n").Add(new Dictionary<string, object>
        {
            ["document_id"] = documentId,
            ["locale"] = locale,
            ["state"] = state,
            ["site_id"] = siteId ?? SiteA,
            ["title"] = title,
            ["slug"] = slug,
        });
    }

    public static void AddDocument(this SpikeDbContext context, Guid documentId, string locale = "en", string status = "published", int draftVersion = 1)
    {
        var now = DateTimeOffset.UtcNow;
        context.Rows(SpikeDbContext.Document).Add(new Dictionary<string, object>
        {
            ["document_id"] = documentId,
            ["locale"] = locale,
            ["type"] = "article",
            ["site_id"] = SiteA,
            ["status"] = status,
            ["draft_version"] = draftVersion,
            ["created_at"] = now,
            ["updated_at"] = now,
        });
    }

    public static void AddReference(this SpikeDbContext context, Guid source, string field, Guid target, int position = 0, int state = 1, string locale = "")
    {
        context.Rows(SpikeDbContext.Reference).Add(new Dictionary<string, object>
        {
            ["source_type"] = "article",
            ["source_document_id"] = source,
            ["state"] = state,
            ["locale"] = locale,
            ["field"] = field,
            ["position"] = position,
            ["target_type"] = "article",
            ["target_id"] = target,
        });
    }
}
