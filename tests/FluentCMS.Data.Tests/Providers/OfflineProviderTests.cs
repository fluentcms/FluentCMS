using System.Linq.Expressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.SqlServer;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Sqlite.RowFilter;

namespace FluentCMS.Data.Tests.Providers;

// PostgreSQL and SQL Server run on a database only in #2711. These tests never open a connection: they check
// the model and the SQL text that EF produces, not how a server behaves.
public class OfflineProviderTests
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddBlock("hero", block => block.Field("heading", "text"))
        .AddCollection("post", type => type
            .Field("publishedOn", "dateTime")
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero" }))
            .Field("metadata", "json", field => field.Indexed()))
        .AddSingleton("brand", type => type.Field("name", "text"))
        .Build();

    private static ServiceProvider PostgreSql() => Host(cms => cms.UsePostgreSql("Host=localhost;Database=never_opened"));

    private static ServiceProvider SqlServer(bool nativeJson = false) => Host(cms => cms.UseSqlServer("Server=localhost;Database=never_opened", nativeJson));

    private static ServiceProvider Host(Action<FluentCmsBuilder> provider) =>
        new ServiceCollection().AddSingleton(Schema).AddFluentCms(provider).BuildServiceProvider();

    private static IModel DesignModel(IServiceProvider services) =>
        services.GetRequiredService<CmsDbContext>().GetService<IDesignTimeModel>().Model;

    private static string? ColumnType(IModel model, string entity, string property) =>
        model.FindEntityType(entity)!.FindProperty(property)!.GetColumnType();

    private static List<string> SchemaHistoryColumns(SystemDbContext context) =>
        [.. context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SchemaHistoryRow))!.GetProperties().Select(property => $"{property.GetColumnName()}:{property.GetColumnType()}")];

    private static string Sql(IServiceProvider services, Func<IJsonSqlDialect, ParameterExpression, Expression> filter)
    {
        var dialect = services.GetRequiredService<IJsonSqlDialect>();
        return services.GetRequiredService<CmsDbContext>().Rows("cms_post").Where(Where(row => filter(dialect, row))).ToQueryString();
    }

    private static MethodCallExpression Sections(ParameterExpression row) => Column<string>(row, "sections");

    private static MethodCallExpression Metadata(ParameterExpression row) => Column<string>(row, "metadata");

    [Fact]
    public void PostgreSqlStoresJsonAsJsonbAndDatesAsTimestamptz()
    {
        using var services = PostgreSql();
        using var scope = services.CreateScope();
        var model = DesignModel(scope.ServiceProvider);

        ColumnType(model, "cms_post", "metadata").ShouldBe("jsonb");
        ColumnType(model, "cms_post", "sections").ShouldBe("jsonb");
        ColumnType(model, "cms_post", "published_on").ShouldBe("timestamp with time zone");
        ColumnType(model, typeof(ContentVersion).FullName!, nameof(ContentVersion.Snapshot)).ShouldBe("jsonb");
        ColumnType(model, typeof(Document).FullName!, nameof(Document.SchedulePublishAt)).ShouldBe("timestamp with time zone");
        ColumnType(model, typeof(Job).FullName!, nameof(Job.Payload)).ShouldBe("jsonb");
        ColumnType(model, typeof(Job).FullName!, nameof(Job.RunAt)).ShouldBe("timestamp with time zone");
        ColumnType(model, typeof(RecurringJob).FullName!, nameof(RecurringJob.Payload)).ShouldBe("jsonb");
    }

    [Fact]
    public void PostgreSqlPutsAGinIndexOnAnIndexedJsonField()
    {
        using var services = PostgreSql();
        using var scope = services.CreateScope();

        var index = DesignModel(scope.ServiceProvider).FindEntityType("cms_post")!.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == "metadata"));

        index.GetMethod().ShouldBe("gin");
    }

    [Fact]
    public void BothProvidersCreateTheSingletonKey()
    {
        using var postgreSql = PostgreSql();
        using var postgreSqlScope = postgreSql.CreateScope();
        using var sqlServer = SqlServer();
        using var sqlServerScope = sqlServer.CreateScope();

        postgreSqlScope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GenerateCreateScript()
            .ShouldContain("CREATE UNIQUE INDEX uq_cms_brand_singleton ON cms_brand (site_id, state);");
        sqlServerScope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GenerateCreateScript()
            .ShouldContain("CREATE UNIQUE INDEX [uq_cms_brand_singleton] ON [cms_brand] ([site_id], [state]);");
    }

    [Fact]
    public void SqlServerStoresCheckedJsonAsNvarcharMax()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();
        var model = DesignModel(scope.ServiceProvider);

        ColumnType(model, "cms_post", "metadata").ShouldBe("nvarchar(max)");
        ColumnType(model, "cms_post", "published_on").ShouldBe("datetimeoffset");
        ColumnType(model, typeof(ContentVersion).FullName!, nameof(ContentVersion.Snapshot)).ShouldBe("nvarchar(max)");
        ColumnType(model, typeof(Document).FullName!, nameof(Document.SchedulePublishAt)).ShouldBe("datetimeoffset");
        ColumnType(model, typeof(Job).FullName!, nameof(Job.Payload)).ShouldBe("nvarchar(max)");
        ColumnType(model, typeof(Job).FullName!, nameof(Job.RunAt)).ShouldBe("datetimeoffset");
        model.FindEntityType("cms_post")!.GetCheckConstraints().Select(check => check.Sql).ShouldContain("ISJSON([metadata]) = 1");
    }

    [Fact]
    public void SqlServerCanUseTheNativeJsonType()
    {
        using var services = SqlServer(nativeJson: true);
        using var scope = services.CreateScope();
        var model = DesignModel(scope.ServiceProvider);

        ColumnType(model, "cms_post", "metadata").ShouldBe("json");
        model.FindEntityType("cms_post")!.GetCheckConstraints().ShouldBeEmpty();
    }

    [Fact]
    public void NeitherProviderMigratesOnStartupByDefault()
    {
        using var postgreSql = PostgreSql();
        using var sqlServer = SqlServer();

        postgreSql.GetRequiredService<IDataProvider>().MigrateOnStartup.ShouldBeFalse();
        sqlServer.GetRequiredService<IDataProvider>().MigrateOnStartup.ShouldBeFalse();
    }

    [Fact]
    public void ThePostgreSqlSystemMigrationsMatchTheModel()
    {
        using var services = PostgreSql();
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database;

        database.GetMigrations().Count().ShouldBe(16);
        database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void TheSqlServerSystemMigrationsMatchTheModel()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database;

        database.GetMigrations().Count().ShouldBe(16);
        database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void ThePostgreSqlSchemaHistoryKeepsTheSnapshotAsText()
    {
        using var services = PostgreSql();
        using var scope = services.CreateScope();

        SchemaHistoryColumns(scope.ServiceProvider.GetRequiredService<SystemDbContext>()).ShouldBe(
            ["id:bigint", "hash:character varying(64)", "snapshot:text", "applied_at:timestamp with time zone", "fluentcms_version:character varying(64)"], ignoreOrder: true);
    }

    [Fact]
    public void TheSqlServerSchemaHistoryKeepsTheSnapshotAsText()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();

        SchemaHistoryColumns(scope.ServiceProvider.GetRequiredService<SystemDbContext>()).ShouldBe(
            ["id:bigint", "hash:nvarchar(64)", "snapshot:nvarchar(max)", "applied_at:datetimeoffset", "fluentcms_version:nvarchar(64)"], ignoreOrder: true);
    }

    [Fact]
    public void EveryProviderHasTheSchemaHistoryMigration()
    {
        using var postgreSql = PostgreSql();
        using var sqlServer = SqlServer();

        foreach (var services in new[] { postgreSql, sqlServer })
        {
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.GetMigrations().ShouldContain(id => id.EndsWith("_AddSchemaHistory", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PostgreSqlFiltersTranslate()
    {
        using var host = PostgreSql();
        using var scope = host.CreateScope();
        var services = scope.ServiceProvider;

        Sql(services, (dialect, row) => dialect.HasBlock(Sections(row), Bind("hero")))
            .ShouldContain("c.sections @> jsonb_build_array(jsonb_build_object('type', @");

        Sql(services, (dialect, row) => dialect.AnyBlock(Sections(row), Bind("hero"), block => dialect.ScalarEquals(block, "heading", JsonScalarType.Text, Bind("x"))))
            .ShouldContain("jsonb_array_elements(");

        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "minutes", JsonScalarType.WholeNumber, Bind(12L)))
            .ShouldContain("c.metadata @> jsonb_build_object('minutes', @");
        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "rating", JsonScalarType.DecimalNumber, Bind(4.5m)))
            .ShouldContain("c.metadata @> jsonb_build_object('rating', @");
        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "featured", JsonScalarType.Boolean, Bind(true)))
            .ShouldContain("c.metadata @> jsonb_build_object('featured', @");

        var contains = Sql(services, (dialect, row) => dialect.ScalarContains(Metadata(row), "audience", Bind("x")));
        contains.ShouldContain("jsonb_typeof(jsonb_extract_path(");
        contains.ShouldContain("jsonb_extract_path_text(c.metadata, 'audience') LIKE @");

        Sql(services, (dialect, row) => dialect.ArrayContains(Metadata(row), "labels", Bind("x")))
            .ShouldContain("@> jsonb_build_object('labels', jsonb_build_array(@");
        Sql(services, (dialect, row) => dialect.ListContains(Metadata(row), Bind("x")))
            .ShouldContain("c.metadata @> jsonb_build_array(@");
    }

    [Fact]
    public void SqlServerFiltersTranslate()
    {
        using var host = SqlServer();
        using var scope = host.CreateScope();
        var services = scope.ServiceProvider;

        Sql(services, (dialect, row) => dialect.HasBlock(Sections(row), Bind("hero")))
            .ShouldContain("OPENJSON([c].[sections])");

        Sql(services, (dialect, row) => dialect.AnyBlock(Sections(row), Bind("hero"), block => dialect.ScalarEquals(block, "heading", JsonScalarType.Text, Bind("x"))))
            .ShouldContain("COLLATE Latin1_General_100_BIN2");

        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "minutes", JsonScalarType.WholeNumber, Bind(12L)))
            .ShouldContain("TRY_CONVERT(bigint, ");
        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "rating", JsonScalarType.DecimalNumber, Bind(4.5m)))
            .ShouldContain("TRY_CONVERT(decimal(38,18), ");
        Sql(services, (dialect, row) => dialect.ScalarEquals(Metadata(row), "featured", JsonScalarType.Boolean, Bind(true)))
            .ShouldContain("N'true'");

        Sql(services, (dialect, row) => dialect.ScalarContains(Metadata(row), "audience", Bind("x")))
            .ShouldContain("COLLATE Latin1_General_100_BIN2");
        Sql(services, (dialect, row) => dialect.ArrayContains(Metadata(row), "labels", Bind("x")))
            .ShouldContain("[key] = N'labels'");
        Sql(services, (dialect, row) => dialect.ListContains(Metadata(row), Bind("x")))
            .ShouldContain("OPENJSON([c].[metadata])");
    }

    [Fact]
    public void TheSystemModelIsSeparatePerSqlServerJsonMode()
    {
        using var text = SqlServer();
        using var native = SqlServer(nativeJson: true);
        using var textScope = text.CreateScope();
        using var nativeScope = native.CreateScope();

        var textModel = textScope.ServiceProvider.GetRequiredService<SystemDbContext>().GetService<IDesignTimeModel>().Model;
        var nativeModel = nativeScope.ServiceProvider.GetRequiredService<SystemDbContext>().GetService<IDesignTimeModel>().Model;

        ColumnType(textModel, typeof(ContentVersion).FullName!, nameof(ContentVersion.Snapshot)).ShouldBe("nvarchar(max)");
        ColumnType(nativeModel, typeof(ContentVersion).FullName!, nameof(ContentVersion.Snapshot)).ShouldBe("json");
    }
}
