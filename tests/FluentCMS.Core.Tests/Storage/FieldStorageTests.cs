using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.FieldTypes;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Storage;

public class FieldStorageTests
{
    private static List<StoredField> ArticleFields()
    {
        var schema = SampleSchema.FromClasses().Build();
        return [.. FieldStorage.Of(schema, schema.FindContentType("article")!)];
    }

    [Theory]
    [InlineData("article", "cms_article")]
    [InlineData("site-settings", "cms_site_settings")]
    [InlineData("a-b-c", "cms_a_b_c")]
    public void TypeNameBecomesTableName(string type, string table)
    {
        var schema = SampleSchema.NewBuilder().AddCollection(type, builder => builder.Localized()).Build();
        var schemaType = schema.FindContentType(type)!;

        StorageNames.Table(schemaType).ShouldBe(table);
        StorageNames.LocaleTable(schemaType).ShouldBe($"{table}_i18n");
    }

    [Theory]
    [InlineData("title", "title")]
    [InlineData("publishedOn", "published_on")]
    [InlineData("videoUrl", "video_url")]
    [InlineData("metaTitle", "meta_title")]
    public void FieldNameBecomesColumnName(string field, string column) => StorageNames.Column(field).ShouldBe(column);

    [Fact]
    public void ArticleFieldsSplitByLocalizable()
    {
        var fields = ArticleFields();

        fields.Where(field => field.Localized).Select(field => field.Column).ShouldBe(["title", "slug", "body"]);
        fields.Where(field => !field.Localized && field.Kind != FieldStorageKind.Reference).Select(field => field.Column)
            .ShouldBe(["sections", "seo_meta_title", "seo_meta_description", "category", "published_on", "video_url"]);
    }

    [Fact]
    public void ArticleFieldsHaveTheKindsOfTheColumnMapping()
    {
        var kinds = ArticleFields().ToDictionary(field => field.Column, field => field.Kind);

        kinds["title"].ShouldBe(FieldStorageKind.Column);
        kinds["body"].ShouldBe(FieldStorageKind.Json);
        kinds["sections"].ShouldBe(FieldStorageKind.Json);
        kinds["author"].ShouldBe(FieldStorageKind.Reference);
        kinds["tags"].ShouldBe(FieldStorageKind.Reference);
        kinds["hero"].ShouldBe(FieldStorageKind.Reference);
    }

    [Fact]
    public void LocalizableComponentFlattensIntoTheLocaleTable()
    {
        var schema = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("metaTitle", "text"))
            .AddCollection("page", type => type.Localized().Field("seo", "component", field => field.Localizable().Setting(FieldSettings.Component, "seo")))
            .Build();

        var field = FieldStorage.Of(schema, schema.FindContentType("page")!).ShouldHaveSingleItem();

        field.Column.ShouldBe("seo_meta_title");
        field.Localized.ShouldBeTrue();
        field.Location.ShouldBe("page.seo.metaTitle");
    }

    [Fact]
    public void MediaFieldIsStoredAsReferences()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("hero", "media", field => field.Multiple())).Build();

        FieldStorage.Of(schema, schema.FindContentType("page")!).ShouldHaveSingleItem().Kind.ShouldBe(FieldStorageKind.Reference);
    }

    [Fact]
    public void PluginFieldTypeIsStoredWhereItsStorageKindSays()
    {
        var custom = new SchemaBuilder(new FieldTypeRegistry([new FreeFormFieldType()])).AddCollection("page", type => type.Field("tint", "freeform")).Build();

        var field = FieldStorage.Of(custom, custom.FindContentType("page")!).ShouldHaveSingleItem();

        field.Kind.ShouldBe(FieldStorageKind.Column);
        field.ClrType.ShouldBe(typeof(string));
    }
}
