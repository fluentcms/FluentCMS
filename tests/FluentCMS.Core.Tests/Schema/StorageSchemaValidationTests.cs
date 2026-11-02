using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema.FieldTypes;
using FluentCMS.Media;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using CollectionAttribute = FluentCMS.Schema.CollectionAttribute;

namespace FluentCMS.Core.Tests.Schema;

// The checks the storage mapping depends on, reported at Build() with the other FCMS12xx errors.
public class StorageSchemaValidationTests
{
    private static readonly string[] Options = ["a", "b"];

    private static SchemaValidationException Fails(SchemaBuilder builder) =>
        Should.Throw<SchemaValidationException>(() => builder.Build());

    [Fact]
    public void FieldThatTakesASystemColumnIsReported()
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("documentId", "text").Field("siteId", "text").Field("state", "integer"));

        var exception = Fails(builder);

        exception.Errors.Select(error => error.Code).ShouldBe([SchemaErrorCodes.ReservedName, SchemaErrorCodes.ReservedName, SchemaErrorCodes.ReservedName]);
        exception.Message.ShouldContain("page.documentId");
        exception.Message.ShouldContain("page.siteId");
        exception.Message.ShouldContain("page.state");
    }

    [Fact]
    public void ReservedFieldNameIsNotReportedTwice()
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", type => type.Localized().Field("locale", "text").Field("id", "text"));

        Fails(builder).Errors.Select(error => error.Code).ShouldBe([SchemaErrorCodes.ReservedName, SchemaErrorCodes.ReservedName]);
    }

    [Fact]
    public void ComponentFieldIsPrefixedSoItCannotTakeASystemColumn()
    {
        var builder = SampleSchema.NewBuilder()
            .AddComponent("flags", type => type.Field("state", "text"))
            .AddCollection("page", type => type.Field("flags", "component", field => field.Setting(FieldSettings.Component, "flags")));

        builder.Build();
    }

    [Fact]
    public void FieldsThatShareAColumnAreReportedByName()
    {
        var builder = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("metaTitle", "text"))
            .AddCollection("page", type => type
                .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo"))
                .Field("seoMetaTitle", "text"));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SchemaErrorCodes.DuplicateField);
        error.Message.ShouldContain("page.seo.metaTitle");
        error.Message.ShouldContain("page.seoMetaTitle");
        error.Message.ShouldContain("seo_meta_title");
    }

    [Fact]
    public void FieldsInDifferentTablesMayShareAColumn()
    {
        var builder = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("metaTitle", "text"))
            .AddCollection("page", type => type
                .Localized()
                .Field("seo", "component", field => field.Localizable().Setting(FieldSettings.Component, "seo"))
                .Field("seoMetaTitle", "text"));

        builder.Build();
    }

    [Fact]
    public void RepeatedFieldNameIsReportedOnce()
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("title", "text").Field("title", "text"));

        Fails(builder).Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.DuplicateField);
    }

    [Theory]
    [InlineData("document", "cms_document")]
    [InlineData("reference", "cms_reference")]
    [InlineData("version", "cms_version")]
    [InlineData("job", "cms_job")]
    [InlineData("job-attempt", "cms_job_attempt")]
    [InlineData("user", "cms_user")]
    [InlineData("media-asset", "cms_media_asset")]
    [InlineData("site-host", "cms_site_host")]
    [InlineData("schema-history", "cms_schema_history")]
    public void TypeThatTakesASystemTableIsReported(string type, string table)
    {
        var exception = Fails(SampleSchema.NewBuilder().AddCollection(type, _ => { }));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.ReservedName);
        exception.Message.ShouldContain(table);
    }

    [Fact]
    public void SingletonThatTakesASystemTableIsReported() =>
        Fails(SampleSchema.NewBuilder().AddSingleton("setting", _ => { })).Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.ReservedName);

    [Fact]
    public void ComponentMayBeNamedLikeASystemTable() =>
        SampleSchema.NewBuilder().AddComponent("job", _ => { }).Build();

    [Fact]
    public void TypeThatTakesALocaleTableIsReported()
    {
        var builder = SampleSchema.NewBuilder()
            .AddCollection("page", type => type.Localized())
            .AddCollection("page-i18n", _ => { });

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SchemaErrorCodes.ReservedName);
        error.Message.ShouldContain("cms_page_i18n");
    }

    [Fact]
    public void TypeBesideAnUnlocalizedTypeMayEndInI18n() =>
        SampleSchema.NewBuilder().AddCollection("page", _ => { }).AddCollection("page-i18n", _ => { }).Build();

    [Theory]
    [InlineData("""{ "name": "body", "type": "richText", "unique": true }""")]
    [InlineData("""{ "name": "data", "type": "json", "unique": true }""")]
    [InlineData("""{ "name": "owner", "type": "relation", "target": "page", "unique": true }""")]
    [InlineData("""{ "name": "tags", "type": "select", "options": ["a", "b"], "multiple": true, "unique": true }""")]
    public void UniqueFieldWithoutAColumnIsReported(string field)
    {
        var builder = SampleSchema.NewBuilder().AddSchemaJson($$"""{ "collections": [ { "name": "page", "fields": [ {{field}} ] } ] }""");

        Fails(builder).Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.InvalidField);
    }

    [Fact]
    public void UniqueMediaFieldIsReported()
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("hero", "media", field => field.Unique()));

        Fails(builder).Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.InvalidField);
    }

    [Fact]
    public void UniqueFieldOfAComponentIsCheckedToo()
    {
        var builder = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("notes", "json", field => field.Unique()))
            .AddCollection("page", type => type.Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo")));

        Fails(builder).Errors.ShouldHaveSingleItem().Message.ShouldContain("page.seo.notes");
    }

    [Fact]
    public void ErrorsOfEveryKindAreReportedTogether()
    {
        var builder = SampleSchema.NewBuilder()
            .AddCollection("job", type => type.Field("state", "text").Field("data", "json", field => field.Unique()));

        Fails(builder).Errors.Select(error => error.Code).Order().ShouldBe([SchemaErrorCodes.ReservedName, SchemaErrorCodes.ReservedName, SchemaErrorCodes.InvalidField]);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("integer")]
    public void MultipleFluentFieldOfASingleValueTypeIsReported(string type)
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", page => page.Field("value", type, field => field.Multiple()));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SchemaErrorCodes.InvalidField);
        error.Message.ShouldContain("page.value");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("integer")]
    public void MultipleSchemaJsonFieldOfASingleValueTypeIsReported(string type)
    {
        var builder = SampleSchema.NewBuilder().AddSchemaJson($$"""{ "collections": [ { "name": "page", "fields": [ { "name": "value", "type": "{{type}}", "multiple": true } ] } ] }""");

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SchemaErrorCodes.InvalidField);
        error.Message.ShouldContain("page.value");
    }

    [Fact]
    public void MultipleAttributeFieldOfASingleValueTypeIsReported()
    {
        var errors = Fails(SampleSchema.NewBuilder().AddContentType<ListsOfScalars>()).Errors;

        errors.Select(error => error.Code).ShouldBe([SchemaErrorCodes.InvalidField, SchemaErrorCodes.InvalidField]);
        errors.Select(error => error.Message).ShouldContain(message => message.Contains("lists-of-scalars.names"));
        errors.Select(error => error.Message).ShouldContain(message => message.Contains("lists-of-scalars.counts"));
    }

    [Fact]
    public void SingleValueTypeWithoutMultipleStillBuilds() =>
        SampleSchema.NewBuilder().AddCollection("page", page => page.Field("value", "text").Field("count", "integer")).Build();

    [Fact]
    public void CoreTypesThatHoldListsAcceptMultiple()
    {
        var schema = SampleSchema.NewBuilder()
            .AddComponent("seo", type => type.Field("title", "text"))
            .AddCollection("tag", _ => { })
            .AddCollection("page", page => page
                .Field("choices", "select", field => field.Multiple().Setting(FieldSettings.Options, Options))
                .Field("related", "relation", field => field.Multiple().Relation("tag"))
                .Field("gallery", "media", field => field.Multiple())
                .Field("addresses", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo")))
            .Build();

        var storage = FieldStorage.Of(schema, schema.FindContentType("page")!).ToDictionary(field => field.Column, field => field.Kind);

        storage["choices"].ShouldBe(FieldStorageKind.Json);
        storage["related"].ShouldBe(FieldStorageKind.Reference);
        storage["gallery"].ShouldBe(FieldStorageKind.Reference);
        storage["addresses"].ShouldBe(FieldStorageKind.Json);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("richText")]
    public void CoreTypesStoredAsJsonStillRejectMultiple(string type)
    {
        var builder = SampleSchema.NewBuilder().AddCollection("page", page => page.Field("value", type, field => field.Multiple()));

        Fails(builder).Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.InvalidField);
    }

    [Fact]
    public void PluginTypeThatSupportsMultipleBuilds()
    {
        var schema = new SchemaBuilder(new FieldTypeRegistry([new ListFieldType()]))
            .AddCollection("page", page => page.Field("items", "list", field => field.Multiple()))
            .Build();

        FieldStorage.Of(schema, schema.FindContentType("page")!).ShouldHaveSingleItem().Kind.ShouldBe(FieldStorageKind.Json);
    }

    [Fact]
    public void PluginTypeThatDoesNotSupportMultipleIsReported()
    {
        var builder = new SchemaBuilder(new FieldTypeRegistry([new FreeFormFieldType()]))
            .AddCollection("page", page => page.Field("tint", "freeform", field => field.Multiple()));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SchemaErrorCodes.InvalidField);
        error.Message.ShouldContain("page.tint");
    }

    [Collection]
    private sealed class ListsOfScalars : ContentItem
    {
        [Text]
        public List<string> Names { get; set; } = [];

        [Integer]
        public List<int> Counts { get; set; } = [];

        [Json]
        public List<string> Notes { get; set; } = [];

        [Media]
        public List<MediaRef> Gallery { get; set; } = [];
    }
}
