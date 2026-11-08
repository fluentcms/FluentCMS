using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class ContentModelTests
{
    private static readonly string[] Options = ["a", "b"];
    private static readonly string[] LongOption = [new string('x', 80)];

    private static SchemaModel Sample => SampleSchema.FromClasses().Build();

    private static IModel ModelOf(SchemaModel schema)
    {
        using var database = new StorageDatabase(schema);
        using var context = database.CreateContext();
        return context.Model;
    }

    private static IEntityType Table(IModel model, string name) => model.FindEntityType(name) ?? throw new ShouldAssertException($"{name} is not in the model");

    private static string[] Columns(IEntityType entity) =>
        [.. entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal)];

    private static string[] IndexColumns(IIndex index) => [.. index.Properties.Select(property => property.GetColumnName())];

    private static IIndex Index(IEntityType entity, string name) =>
        entity.GetIndexes().SingleOrDefault(index => index.GetDatabaseName() == name) ?? throw new ShouldAssertException($"{name} is not on {entity.Name}");

    [Fact]
    public void ArticleProducesTheSharedTableOfAppendixA()
    {
        var table = Table(ModelOf(Sample), "cms_article");

        table.GetTableName().ShouldBe("cms_article");

        // Appendix A has video_url in the locale table, but the sample of section 4.2 does not mark it [Localizable].
        Columns(table).ShouldBe(["category", "document_id", "id", "published_on", "sections", "seo_meta_description", "seo_meta_title", "site_id", "state", "video_url"]);
        table.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["id"]);

        table.FindProperty("site_id")!.IsNullable.ShouldBeFalse();
        table.FindProperty("state")!.ClrType.ShouldBe(typeof(short));

        var category = table.FindProperty("category")!;
        category.GetMaxLength().ShouldBe(64);
        category.IsNullable.ShouldBeTrue();

        table.FindProperty("published_on")!.ClrType.ShouldBe(typeof(DateTimeOffset?));

        var sections = table.FindProperty("sections")!;
        sections.ClrType.ShouldBe(typeof(string));
        sections.IsNullable.ShouldBeFalse();
        sections.GetDefaultValue().ShouldBe("[]");

        table.FindProperty("seo_meta_title")!.GetMaxLength().ShouldBe(70);
        table.FindProperty("seo_meta_title")!.IsNullable.ShouldBeTrue();
        table.FindProperty("seo_meta_description")!.GetMaxLength().ShouldBe(160);

        table.FindProperty("video_url")!.GetMaxLength().ShouldBe(2048);
        table.FindProperty("video_url")!.IsNullable.ShouldBeTrue();

        var key = Index(table, "uq_cms_article_document_state");
        key.IsUnique.ShouldBeTrue();
        IndexColumns(key).ShouldBe(["document_id", "state"]);

        var indexed = Index(table, "ix_cms_article_category");
        indexed.IsUnique.ShouldBeFalse();
        IndexColumns(indexed).ShouldBe(["site_id", "state", "category"]);
        IndexColumns(Index(table, "ix_cms_article_published_on")).ShouldBe(["site_id", "state", "published_on"]);
    }

    [Fact]
    public void ArticleProducesTheLocaleTableOfAppendixA()
    {
        var table = Table(ModelOf(Sample), "cms_article_i18n");

        table.GetTableName().ShouldBe("cms_article_i18n");
        Columns(table).ShouldBe(["body", "document_id", "id", "locale", "site_id", "slug", "state", "title"]);
        table.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["id"]);

        table.FindProperty("locale")!.GetMaxLength().ShouldBe(16);
        table.FindProperty("locale")!.IsNullable.ShouldBeFalse();

        foreach (var column in new[] { "title", "slug", "body" })
        {
            table.FindProperty(column)!.IsNullable.ShouldBeFalse(column);
        }

        table.FindProperty("title")!.GetMaxLength().ShouldBe(200);
        table.FindProperty("slug")!.GetMaxLength().ShouldBe(200);

        var key = Index(table, "uq_cms_article_i18n_document_locale_state");
        key.IsUnique.ShouldBeTrue();
        IndexColumns(key).ShouldBe(["document_id", "locale", "state"]);

        var slug = Index(table, "ux_cms_article_i18n_slug");
        slug.IsUnique.ShouldBeTrue();
        slug.GetFilter().ShouldBe("state = 1");
        IndexColumns(slug).ShouldBe(["site_id", "locale", "slug"]);
    }

    [Fact]
    public void RelationsAndMediaHaveNoColumn()
    {
        var model = ModelOf(Sample);

        foreach (var column in new[] { "author", "tags", "hero" })
        {
            Table(model, "cms_article").FindProperty(column).ShouldBeNull();
            Table(model, "cms_article_i18n").FindProperty(column).ShouldBeNull();
        }

        Table(model, "cms_author").FindProperty("avatar").ShouldBeNull();
    }

    [Fact]
    public void GeneratedScriptHasTheTablesAndIndexes()
    {
        using var database = new StorageDatabase(Sample);
        using var context = database.CreateCreatedContext();

        var script = context.Database.GenerateCreateScript();

        script.ShouldContain("CREATE TABLE \"cms_article\"");
        script.ShouldContain("CREATE TABLE \"cms_article_i18n\"");
        script.ShouldContain("\"sections\" TEXT NOT NULL DEFAULT '[]'");
        script.ShouldContain("CREATE UNIQUE INDEX \"ux_cms_article_i18n_slug\" ON \"cms_article_i18n\" (\"site_id\", \"locale\", \"slug\") WHERE state = 1");
        script.ShouldContain("CREATE INDEX \"ix_cms_article_category\" ON \"cms_article\" (\"site_id\", \"state\", \"category\")");
    }

    [Fact]
    public void NonLocalizedTypeProducesOneTable()
    {
        var model = ModelOf(Sample);

        Columns(Table(model, "cms_author")).ShouldBe(["bio", "document_id", "id", "name", "site_id", "state"]);
        model.FindEntityType("cms_author_i18n").ShouldBeNull();
        Table(model, "cms_author").FindProperty("name")!.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void KebabCaseTypeBecomesSnakeCaseTable()
    {
        var table = Table(ModelOf(Sample), "cms_site_settings");

        table.GetTableName().ShouldBe("cms_site_settings");
        Columns(table).ShouldBe(["description", "document_id", "id", "site_id", "site_name", "state"]);
    }

    [Fact]
    public void GlobalTypeLeavesSiteOutOfItsIndexes()
    {
        var table = Table(ModelOf(Sample), "cms_tag");

        table.FindProperty("site_id")!.IsNullable.ShouldBeTrue();

        var slug = Index(table, "ux_cms_tag_slug");
        slug.GetFilter().ShouldBe("state = 1");
        IndexColumns(slug).ShouldBe(["slug"]);
    }

    [Fact]
    public void LocalizedGlobalTypeScopesItsUniqueIndexByLocale()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("term", type => type
            .Localized()
            .Scope(ContentScope.Global)
            .Field("label", "text", field => field.Localizable().Unique().Indexed()))
            .Build();

        var table = Table(ModelOf(schema), "cms_term_i18n");

        table.FindProperty("site_id")!.IsNullable.ShouldBeTrue();
        IndexColumns(Index(table, "ux_cms_term_i18n_label")).ShouldBe(["locale", "label"]);
        IndexColumns(Index(table, "ix_cms_term_i18n_label")).ShouldBe(["locale", "state", "label"]);
    }

    [Fact]
    public void SiteScopedNonLocalizedUniqueIsScopedBySite()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("path", "text", field => field.Unique())).Build();

        IndexColumns(Index(Table(ModelOf(schema), "cms_page"), "ux_cms_page_path")).ShouldBe(["site_id", "path"]);
    }

    [Fact]
    public void LocalizableIndexedFieldIsIndexedPerLocale()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type
            .Localized()
            .Field("title", "text", field => field.Localizable().Indexed()))
            .Build();

        IndexColumns(Index(Table(ModelOf(schema), "cms_page_i18n"), "ix_cms_page_i18n_title")).ShouldBe(["site_id", "locale", "state", "title"]);
    }

    [Fact]
    public void ASelectColumnKeepsItsWidthWhenAnOptionIsAdded()
    {
        SchemaModel Select(params string[] options) => SampleSchema.NewBuilder()
            .AddCollection("sample", type => type.Field("choice", "select", field => field.Setting(FieldSettings.Options, options)))
            .Build();

        Table(ModelOf(Select("a")), "cms_sample").FindProperty("choice")!.GetMaxLength().ShouldBe(64);
        Table(ModelOf(Select("a", new string('x', 64))), "cms_sample").FindProperty("choice")!.GetMaxLength().ShouldBe(64);
    }

    [Fact]
    public void FieldTypesMapToTheirColumns()
    {
        var schema = SampleSchema.NewBuilder()
            .AddComponent("address", type => type
                .Field("streetName", "text", field => field.Required())
                .Field("geo", "component", field => field.Setting(FieldSettings.Component, "point")))
            .AddComponent("point", type => type.Field("lat", "decimal").Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, Options)))
            .AddCollection("sample", type => type
                .Field("short", "text", field => field.Length(null, 10))
                .Field("unbounded", "text")
                .Field("long", "longText")
                .Field("mail", "email")
                .Field("site", "url")
                .Field("count", "integer", field => field.Required())
                .Field("price", "decimal", field => field.Setting(FieldSettings.Precision, 10L).Setting(FieldSettings.Scale, 3L))
                .Field("ratio", "decimal")
                .Field("active", "boolean")
                .Field("day", "date")
                .Field("at", "dateTime")
                .Field("choice", "select", field => field.Length(null, 80).Setting(FieldSettings.Options, LongOption))
                .Field("choices", "select", field => field.Multiple().Setting(FieldSettings.Options, Options))
                .Field("data", "json")
                .Field("home", "component", field => field.Setting(FieldSettings.Component, "address"))
                .Field("others", "component", field => field.Multiple().Setting(FieldSettings.Component, "address")))
            .Build();

        var table = Table(ModelOf(schema), "cms_sample");

        void Column(string name, Type clrType, bool nullable = true, int? maxLength = null, string? defaultValue = null)
        {
            var property = table.FindProperty(name) ?? throw new ShouldAssertException($"column {name} is missing");
            property.ClrType.ShouldBe(clrType, name);
            property.IsNullable.ShouldBe(nullable, name);
            property.GetMaxLength().ShouldBe(maxLength, name);
            property.GetDefaultValue().ShouldBe(defaultValue, name);
        }

        Column("short", typeof(string), maxLength: 10);
        Column("unbounded", typeof(string));
        Column("long", typeof(string));
        Column("mail", typeof(string), maxLength: 254);
        Column("site", typeof(string), maxLength: 2048);
        Column("count", typeof(long?));
        Column("price", typeof(decimal?));
        Column("ratio", typeof(decimal?));
        Column("active", typeof(bool?));
        Column("day", typeof(DateOnly?));
        Column("at", typeof(DateTimeOffset?));
        Column("choice", typeof(string), maxLength: 80);
        Column("choices", typeof(string), nullable: false, defaultValue: "[]");
        Column("data", typeof(string));
        Column("others", typeof(string), nullable: false, defaultValue: "[]");

        // A required field of a component stays nullable, because the component may be absent.
        Column("home_street_name", typeof(string));
        Column("home_geo_lat", typeof(decimal?));
        Column("home_geo_labels", typeof(string), nullable: false, defaultValue: "[]");
        table.FindProperty("home").ShouldBeNull();

        table.FindProperty("price")!.GetPrecision().ShouldBe(10);
        table.FindProperty("price")!.GetScale().ShouldBe(3);
        table.FindProperty("ratio")!.GetPrecision().ShouldBeNull();
    }

    [Fact]
    public void IndexesOnAComponentFieldUseItsFlattenedColumn()
    {
        var schema = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("metaTitle", "text", field => field.Indexed().Unique()))
            .AddCollection("page", type => type.Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo")))
            .Build();

        var table = Table(ModelOf(schema), "cms_page");

        IndexColumns(Index(table, "ix_cms_page_seo_meta_title")).ShouldBe(["site_id", "state", "seo_meta_title"]);

        var unique = Index(table, "ux_cms_page_seo_meta_title");
        unique.IsUnique.ShouldBeTrue();
        unique.GetFilter().ShouldBe("state = 1 AND \"seo_meta_title\" IS NOT NULL");
        IndexColumns(unique).ShouldBe(["site_id", "seo_meta_title"]);
    }

    [Fact]
    public void IndexedRelationAndJsonFieldsGetNoNeutralIndex()
    {
        var schema = SampleSchema.NewBuilder().AddSchemaJson("""
            { "collections": [ { "name": "page", "fields": [
                { "name": "owner", "type": "relation", "target": "page", "indexed": true },
                { "name": "data", "type": "json", "indexed": true }
            ] } ] }
            """).Build();

        Table(ModelOf(schema), "cms_page").GetIndexes().Select(index => index.GetDatabaseName()).ShouldBe(["ix_cms_page_state", "uq_cms_page_document_state"], ignoreOrder: true);
    }

    [Fact]
    public void ASingletonHasOneSharedRowPerSiteAndState()
    {
        var schema = SampleSchema.NewBuilder().AddSchemaJson("""
            { "collections": [ { "name": "page", "fields": [ { "name": "title", "type": "text" } ] } ],
              "singletons": [
                { "name": "home", "localized": true, "fields": [ { "name": "title", "type": "text", "localizable": true } ] },
                { "name": "brand", "scope": "global", "fields": [ { "name": "name", "type": "text" } ] } ] }
            """).Build();
        var model = ModelOf(schema);

        var home = Index(Table(model, "cms_home"), "uq_cms_home_singleton");
        home.IsUnique.ShouldBeTrue();
        home.GetFilter().ShouldBeNull();
        IndexColumns(home).ShouldBe(["site_id", "state"]);

        var brand = Index(Table(model, "cms_brand"), "uq_cms_brand_singleton");
        brand.IsUnique.ShouldBeTrue();
        IndexColumns(brand).ShouldBe(["state"]);

        Table(model, "cms_home").GetIndexes().Select(index => index.GetDatabaseName()).ShouldBe(["uq_cms_home_document_state", "uq_cms_home_singleton"], ignoreOrder: true);
        Table(model, "cms_home_i18n").GetIndexes().Select(index => index.GetDatabaseName()).ShouldBe(["ix_cms_home_i18n_state", "uq_cms_home_i18n_document_locale_state"], ignoreOrder: true);
        Table(model, "cms_page").GetIndexes().Select(index => index.GetDatabaseName()).ShouldBe(["ix_cms_page_state", "uq_cms_page_document_state"], ignoreOrder: true);
    }

    [Fact]
    public void EveryContentTableHasAStateIndexScopedLikeItsFieldIndexes()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("page", type => type.Localized().Field("title", "text", field => field.Localizable()))
            .AddCollection("term", type => type.Localized().Scope(ContentScope.Global).Field("label", "text", field => field.Localizable()))
            .Build();
        var model = ModelOf(schema);

        IIndex State(string table) => Index(Table(model, table), $"ix_{table}_state");

        IndexColumns(State("cms_page")).ShouldBe(["site_id", "state"]);
        IndexColumns(State("cms_page_i18n")).ShouldBe(["site_id", "locale", "state"]);
        IndexColumns(State("cms_term")).ShouldBe(["state"]);
        IndexColumns(State("cms_term_i18n")).ShouldBe(["locale", "state"]);
        State("cms_page").IsUnique.ShouldBeFalse();
        State("cms_page").GetFilter().ShouldBeNull();
    }

    [Fact]
    public void ANullableUniqueColumnLeavesNullOutOfItsIndex()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type
            .Field("title", "text", field => field.Required().Unique())
            .Field("path", "slug", field => field.Unique())
            .Field("contact", "email", field => field.Required().Unique())
            .Field("rank", "integer", field => field.Unique()))
            .Build();
        var table = Table(ModelOf(schema), "cms_page");

        Index(table, "ux_cms_page_title").GetFilter().ShouldBe("state = 1");
        Index(table, "ux_cms_page_path").GetFilter().ShouldBe("state = 1");
        Index(table, "ux_cms_page_contact").GetFilter().ShouldBe("state = 1 AND \"contact\" IS NOT NULL");
        Index(table, "ux_cms_page_rank").GetFilter().ShouldBe("state = 1 AND \"rank\" IS NOT NULL");
    }

    [Fact]
    public void NamesOverTheIdentifierBudgetAreShortenedApart()
    {
        using var database = new StorageDatabase(SampleSchema.LongNames().Build(), new SqliteLikeConventions());
        using var context = database.CreateCreatedContext();
        var table = Table(context.GetService<IDesignTimeModel>().Model, "cms_customer_testimonial_highlight_i18n");
        const string column = "hero_section_configuration_background_image";

        string[] names = [.. table.GetIndexes().Select(index => index.GetDatabaseName()!), .. table.GetCheckConstraints().Select(check => check.Name!)];

        names.ShouldAllBe(name => name.Length <= StorageNames.MaxIdentifierLength);
        names.ShouldBeUnique();
        IndexColumns(Index(table, StorageNames.Identifier($"ux_{table.GetTableName()}_{column}_alt_text"))).ShouldBe(["site_id", "locale", $"{column}_alt_text"]);
        IndexColumns(Index(table, StorageNames.Identifier($"ux_{table.GetTableName()}_{column}_caption"))).ShouldBe(["site_id", "locale", $"{column}_caption"]);
        IndexColumns(Index(table, StorageNames.Identifier($"ix_{table.GetTableName()}_{column}_caption"))).ShouldBe(["site_id", "locale", "state", $"{column}_caption"]);
        IndexColumns(Index(table, StorageNames.Identifier($"gin_{table.GetTableName()}_{column}_metadata"))).ShouldBe([$"{column}_metadata"]);
        Index(table, StorageNames.Identifier($"uq_{table.GetTableName()}_document_locale_state")).IsUnique.ShouldBeTrue();
        names.ShouldContain(StorageNames.Identifier($"ck_{table.GetTableName()}_{column}_metadata_json"));
        names.ShouldContain("ix_cms_customer_testimonial_highlight_i18n_state");
    }

    [Fact]
    public void ContextWithoutASchemaSaysSo()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>().UseSqlite("Data Source=:memory:").Options;

        Should.Throw<InvalidOperationException>(() => new CmsDbContext(options)).Message.ShouldContain("UseCmsModel");
    }
}
