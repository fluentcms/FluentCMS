using System.Data.SqlTypes;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.SqlServer;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Query.RowFilter;

namespace FluentCMS.Data.Tests.Providers;

// PostgreSQL and SQL Server run on a database only in #2711. These tests never open a connection: they check
// the model and the SQL text that EF produces, not how a server behaves.
public partial class OfflineProviderTests
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddBlock("hero", block => block.Field("heading", "text"))
        .AddCollection("post", type => type
            .Field("publishedOn", "dateTime")
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero" }))
            .Field("metadata", "json", field => field.Indexed())
            .Field("contact", "email", field => field.Unique()))
        .AddSingleton("brand", type => type.Field("name", "text"))
        .Build();

    // A column of each kind that the information schema describes with a size.
    private static readonly SchemaModel TypedSchema = SampleSchema.NewBuilder()
        .AddCollection("post", type => type
            .Field("contact", "email")
            .Field("metadata", "json")
            .Field("price", "decimal", field => field.Setting(FieldSettings.Precision, 10L).Setting(FieldSettings.Scale, 3L))
            .Field("publishedOn", "dateTime"))
        .Build();

    private static ServiceProvider PostgreSql(SchemaModel? schema = null) => Host(cms => cms.UsePostgreSql("Host=localhost;Database=never_opened"), schema);

    private static ServiceProvider SqlServer(bool nativeJson = false, SchemaModel? schema = null) => Host(cms => cms.UseSqlServer("Server=localhost;Database=never_opened", nativeJson), schema);

    private static ServiceProvider Host(Action<FluentCmsBuilder> provider, SchemaModel? schema) =>
        new ServiceCollection().AddSingleton(schema ?? Schema).AddFluentCms(provider).BuildServiceProvider();

    private static string ContentScript(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GenerateCreateScript();
    }

    [GeneratedRegex("""(?:INDEX|CONSTRAINT) [\["]?(?<name>\w+)""")]
    private static partial Regex IndexOrConstraintName();

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
    public void BothProvidersCreateTheListAndTrashIndexes()
    {
        using var postgreSql = PostgreSql();
        using var postgreSqlScope = postgreSql.CreateScope();
        using var sqlServer = SqlServer();
        using var sqlServerScope = sqlServer.CreateScope();

        var postgreSqlSystem = postgreSqlScope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.GenerateCreateScript();
        postgreSqlSystem.ShouldContain("CREATE INDEX ix_cms_document_trash ON cms_document (type, deleted_at) WHERE deleted_at IS NOT NULL;");
        postgreSqlSystem.ShouldContain("CREATE INDEX ix_cms_document_type_site ON cms_document (type, site_id, deleted_at);");
        postgreSqlScope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GenerateCreateScript()
            .ShouldContain("CREATE INDEX ix_cms_post_state ON cms_post (site_id, state);");

        var sqlServerSystem = sqlServerScope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.GenerateCreateScript();
        sqlServerSystem.ShouldContain("CREATE INDEX [ix_cms_document_trash] ON [cms_document] ([type], [deleted_at]) WHERE deleted_at IS NOT NULL;");
        sqlServerSystem.ShouldContain("CREATE INDEX [ix_cms_document_type_site] ON [cms_document] ([type], [site_id], [deleted_at]);");
        sqlServerScope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GenerateCreateScript()
            .ShouldContain("CREATE INDEX [ix_cms_post_state] ON [cms_post] ([site_id], [state]);");
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

        database.GetMigrations().Count().ShouldBe(17);
        database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void TheSqlServerSystemMigrationsMatchTheModel()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database;

        database.GetMigrations().Count().ShouldBe(17);
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
    public void BothProvidersFitTheNamesOfTheirIndexesAndChecks()
    {
        using var postgreSql = PostgreSql(SampleSchema.LongNames().Build());
        using var sqlServer = SqlServer(schema: SampleSchema.LongNames().Build());

        foreach (var script in new[] { ContentScript(postgreSql), ContentScript(sqlServer) })
        {
            var names = IndexOrConstraintName().Matches(script).Select(match => match.Groups["name"].Value).ToList();

            names.ShouldAllBe(name => name.Length <= StorageNames.MaxIdentifierLength);
            names.ShouldBeUnique();
            names.Count(name => name.StartsWith("ux_cms_customer_testimonial_highlight_i18n_hero_sectio", StringComparison.Ordinal)).ShouldBe(2);
        }

        const string metadata = "cms_customer_testimonial_highlight_i18n_hero_section_configuration_background_image_metadata";
        ContentScript(postgreSql).ShouldContain($"CREATE INDEX {StorageNames.Identifier($"gin_{metadata}")} ON");
        ContentScript(sqlServer).ShouldContain($"CONSTRAINT [{StorageNames.Identifier($"ck_{metadata}_json")}] CHECK");
    }

    [Fact]
    public void BothProvidersLeaveNullOutOfANullableUniqueIndex()
    {
        using var postgreSql = PostgreSql();
        using var sqlServer = SqlServer();

        ContentScript(postgreSql).ShouldContain("CREATE UNIQUE INDEX ux_cms_post_contact ON cms_post (site_id, contact) WHERE state = 1 AND \"contact\" IS NOT NULL;");
        ContentScript(sqlServer).ShouldContain("CREATE UNIQUE INDEX [ux_cms_post_contact] ON [cms_post] ([site_id], [contact]) WHERE state = 1 AND \"contact\" IS NOT NULL;");
    }

    [Fact]
    public void SqlServerComparesContentTextUnderABinaryCollation()
    {
        using var sqlServer = SqlServer();
        using var postgreSql = PostgreSql();
        using var sqlServerScope = sqlServer.CreateScope();
        using var postgreSqlScope = postgreSql.CreateScope();
        var model = DesignModel(sqlServerScope.ServiceProvider);

        model.FindEntityType("cms_post")!.FindProperty("contact")!.GetCollation().ShouldBe("Latin1_General_100_BIN2");
        model.FindEntityType("cms_brand")!.FindProperty("name")!.GetCollation().ShouldBe("Latin1_General_100_BIN2");
        model.FindEntityType("cms_post")!.FindProperty("metadata")!.GetCollation().ShouldBeNull();
        model.FindEntityType(typeof(Document).FullName!)!.FindProperty(nameof(Document.Type))!.GetCollation().ShouldBeNull();
        ContentScript(sqlServer).ShouldContain("[contact] nvarchar(254) COLLATE Latin1_General_100_BIN2 NULL");

        DesignModel(postgreSqlScope.ServiceProvider).FindEntityType("cms_post")!.FindProperty("contact")!.GetCollation().ShouldBeNull();
    }

    [Fact]
    public void SqlServerStoresDatesInUtc()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CmsDbContext>().Model;
        var local = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        foreach (var property in new[] { model.FindEntityType("cms_post")!.FindProperty("published_on")!, model.FindEntityType(typeof(Document))!.FindProperty(nameof(Document.SchedulePublishAt))! })
        {
            var stored = (DateTimeOffset)property.GetTypeMapping().Converter!.ConvertToProvider(local)!;

            stored.Offset.ShouldBe(TimeSpan.Zero);
            stored.ShouldBe(local);
        }
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

    [Fact]
    public void SqlServerGeneratesKeysThatRiseInTheOrderOfUniqueidentifier()
    {
        using var services = SqlServer();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var documents = Enumerable.Range(0, 5).Select(_ => context.Add(new Document()).Entity.Id).ToList();
        var rows = Enumerable.Range(0, 5).Select(_ => (Guid)context.Set<Dictionary<string, object>>("cms_post").Add(new Dictionary<string, object>()).Entity["id"]).ToList();

        documents.Select(id => new SqlGuid(id)).ShouldBeInOrder(SortDirection.Ascending);
        rows.Select(id => new SqlGuid(id)).ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public void PostgreSqlGeneratesVersion7Keys()
    {
        using var services = PostgreSql();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        context.Add(new Document()).Entity.Id.Version.ShouldBe(7);
        ((Guid)context.Set<Dictionary<string, object>>("cms_post").Add(new Dictionary<string, object>()).Entity["id"]).Version.ShouldBe(7);
    }

    [Fact]
    public async Task TheInformationSchemaReaderGivesThePostgreSqlStoreTypesOfTheModel()
    {
        using var services = PostgreSql(TypedSchema);
        var read = await InformationSchemaAsync("public", services,
            ("contact", "character varying", 254, null, null),
            ("metadata", "jsonb", null, null, null),
            ("price", "numeric", null, 10, 3),
            ("published_on", "timestamp with time zone", null, null, null),
            ("state", "smallint", null, 16, 0),
            ("id", "uuid", null, null, null));

        read.ShouldBe(["contact:character varying(254)", "metadata:jsonb", "price:numeric(10,3)", "published_on:timestamp with time zone", "state:smallint", "id:uuid"]);
    }

    [Fact]
    public async Task TheInformationSchemaReaderGivesTheSqlServerStoreTypesOfTheModel()
    {
        using var services = SqlServer(schema: TypedSchema);
        var read = await InformationSchemaAsync("dbo", services,
            ("contact", "nvarchar", 254, null, null),
            ("metadata", "nvarchar", -1, null, null),
            ("price", "decimal", null, 10, 3),
            ("published_on", "datetimeoffset", null, null, null),
            ("state", "smallint", null, 5, 0),
            ("id", "uniqueidentifier", null, null, null));

        read.ShouldBe(["contact:nvarchar(254)", "metadata:nvarchar(max)", "price:decimal(10,3)", "published_on:datetimeoffset", "state:smallint", "id:uniqueidentifier"]);
    }

    // INFORMATION_SCHEMA.COLUMNS as the server gives it for these columns of cms_post, played by an attached SQLite database,
    // and a column of another schema that the reader leaves out. Each column read is checked against the model's store type.
    private static async Task<List<string>> InformationSchemaAsync(string schema, ServiceProvider services, params (string Column, string DataType, int? Length, int? Precision, int? Scale)[] columns)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, "ATTACH DATABASE ':memory:' AS INFORMATION_SCHEMA");
        await ExecuteAsync(connection, "CREATE TABLE INFORMATION_SCHEMA.COLUMNS (TABLE_SCHEMA TEXT, TABLE_NAME TEXT, COLUMN_NAME TEXT, DATA_TYPE TEXT, IS_NULLABLE TEXT, CHARACTER_MAXIMUM_LENGTH INTEGER, NUMERIC_PRECISION INTEGER, NUMERIC_SCALE INTEGER)");
        foreach (var (column, dataType, length, precision, scale) in columns.Append(("title", "text", null, null, null)))
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO INFORMATION_SCHEMA.COLUMNS VALUES ($schema, 'cms_post', $column, $type, 'YES', $length, $precision, $scale)",
                ("$schema", column == "title" ? "other" : schema), ("$column", column), ("$type", dataType), ("$length", length), ("$precision", precision), ("$scale", scale));
        }

        var table = (await new InformationSchemaReader($"'{schema}'").ReadAsync(connection, TestContext.Current.CancellationToken)).Tables["cms_post"];

        using var scope = services.CreateScope();
        var model = DesignModel(scope.ServiceProvider).FindEntityType("cms_post")!;
        foreach (var column in table.Columns)
        {
            column.StoreType.ShouldBe(model.GetProperties().Single(property => property.GetColumnName() == column.Name).GetColumnType(), column.Name);
        }

        return [.. table.Columns.Select(column => $"{column.Name}:{column.StoreType}")];
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
