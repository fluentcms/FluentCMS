using System.Text.Json.Nodes;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class SettingsValidationTests
{
    private static readonly string[] Layers = ["a", "b"];

    private static SchemaError[] Errors(SchemaBuilder builder) =>
        Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.ToArray();

    private static SchemaBuilder FromJson(string fieldJson) =>
        new SchemaBuilder().AddSchemaJson($$"""{"collections":[{"name":"article","fields":[{{fieldJson}}]}]}""");

    [Fact]
    public void ALowerCasedKeyInSchemaJsonIsReported()
    {
        var error = Errors(FromJson("""{"name":"title","type":"text","maxlength":10}""")).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("text field article.title has an unknown setting 'maxlength'; it accepts none");
    }

    [Fact]
    public void AMisspeltFlagInSchemaJsonIsReported()
    {
        var error = Errors(FromJson("""{"name":"title","type":"text","requird":true}""")).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldContain("'requird'");
    }

    [Fact]
    public void EveryUnknownKeyIsReportedInKeyOrder()
    {
        var errors = Errors(FromJson("""{"name":"title","type":"text","requird":true,"maxlength":10}"""));

        errors.Select(error => error.Code).ShouldBe(["FCMS1211", "FCMS1211"]);
        errors[0].Message.ShouldContain("'maxlength'");
        errors[1].Message.ShouldContain("'requird'");
    }

    [Fact]
    public void TheMessageListsWhatTheTypeAccepts()
    {
        var error = Errors(FromJson("""{"name":"price","type":"decimal","presision":5}""")).ShouldHaveSingleItem();

        error.Message.ShouldBe("decimal field article.price has an unknown setting 'presision'; it accepts precision, scale");
    }

    [Fact]
    public void TheKnownKeysAndRealPropertiesAreAccepted()
    {
        var model = FromJson("""{"name":"title","type":"text","maxLength":10,"required":true}""").Build();

        model.FindContentType("article")!.Fields.Single().MaxLength.ShouldBe(10);
    }

    [Fact]
    public void AnUnknownKeyOnAFluentFieldIsReported()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("title", "text", field => field.Setting("colour", "red")));

        Errors(builder).ShouldHaveSingleItem().Message.ShouldContain("unknown setting 'colour'");
    }

    [Fact]
    public void AValueOfTheWrongKindIsReported()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("price", "decimal", field => field.Setting(FieldSettings.Precision, "five"))
            .Field("slug", "slug", field => field.Setting(FieldSettings.Source, 5))
            .Field("title", "text"));

        var errors = Errors(builder);

        errors.Select(error => error.Code).ShouldBe(["FCMS1211", "FCMS1211"]);
        errors[0].Message.ShouldBe("setting 'precision' of decimal field article.price must be a whole number");
        errors[1].Message.ShouldBe("setting 'source' of slug field article.slug must be a string");
    }

    [Fact]
    public void AListSettingMustBeAList()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("category", "select", field => field.Setting(FieldSettings.Options, "news")));

        Errors(builder).ShouldContain(error => error.Code == "FCMS1211" && error.Message == "setting 'options' of select field article.category must be a list of strings");
    }

    [Fact]
    public void AMissingRequiredKeyIsReportedWithTheTypeFieldAndKey()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor("provider", FieldSettingKind.String, Required: true)])]);
        var builder = new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map"));

        var error = Errors(builder).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("map field place.location is missing the required setting 'provider'");
    }

    [Fact]
    public void ARequiredKeyThatIsSetBuilds()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor("provider", FieldSettingKind.String, Required: true), new FieldSettingDescriptor("zoom", FieldSettingKind.Integer)])]);
        var builder = new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map", field => field.Setting("provider", "osm").Setting("zoom", 12)));

        builder.Build().FindContentType("place")!.Fields.Single().Settings["provider"].ShouldBe("osm");
    }

    [Fact]
    public void AKindOfTheDecimalSettingAcceptsWholeAndFractionalNumbers()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("ratio", [new FieldSettingDescriptor("step", FieldSettingKind.Decimal), new FieldSettingDescriptor("live", FieldSettingKind.Boolean)])]);

        new SchemaBuilder(registry).AddCollection("a", type => type.Field("x", "ratio", field => field.Setting("step", 1).Setting("live", true))).Build();
        new SchemaBuilder(registry).AddCollection("a", type => type.Field("x", "ratio", field => field.Setting("step", 0.5m))).Build();
        Errors(new SchemaBuilder(registry).AddCollection("a", type => type.Field("x", "ratio", field => field.Setting("live", "yes"))))
            .ShouldHaveSingleItem().Message.ShouldBe("setting 'live' of ratio field a.x must be true or false");
    }

    [Fact]
    public void ATypeWithoutDeclaredSettingsRejectsAnyKeyUnlessItAcceptsAny()
    {
        var strict = new FieldTypeRegistry([new PluginFieldType("map", [])]);
        var open = new FieldTypeRegistry([new PluginFieldType("map", [], acceptsAnySetting: true)]);

        Errors(new SchemaBuilder(strict).AddCollection("place", type => type.Field("location", "map", field => field.Setting("zoom", 12))))
            .ShouldHaveSingleItem().Message.ShouldBe("map field place.location has an unknown setting 'zoom'; it accepts none");

        var model = new SchemaBuilder(open).AddCollection("place", type => type.Field("location", "map", field => field.Setting("zoom", 12).Setting("layers", Layers))).Build();
        model.FindContentType("place")!.Fields.Single().Settings.Keys.ShouldBe(["layers", "zoom"]);
    }

    [Fact]
    public void ATypeThatAcceptsAnySettingStillChecksTheRequiredOnes()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor("provider", FieldSettingKind.String, Required: true)], acceptsAnySetting: true)]);

        Errors(new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map", field => field.Setting("zoom", 12))))
            .ShouldHaveSingleItem().Message.ShouldContain("missing the required setting 'provider'");
    }

    [Fact]
    public void AnUnregisteredTypeIsReportedOnceWithoutSettingErrors()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("hero", "media", field => field.Setting("anything", 1)));

        Errors(builder).ShouldHaveSingleItem().Code.ShouldBe("FCMS1203");
    }

    [Fact]
    public void AMediaTypeThatDeclaresNoSettingsRejectsOne()
    {
        var builder = new SchemaBuilder(new FieldTypeRegistry([new MediaFieldType()]))
            .AddCollection("article", type => type.Field("hero", "media", field => field.Setting("folder", "heroes")));

        Errors(builder).ShouldHaveSingleItem().Message.ShouldBe("media field article.hero has an unknown setting 'folder'; it accepts none");
    }

    [Fact]
    public void TheTypeSpecificChecksAreNotReportedTwice()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("category", "select")
            .Field("seo", "component")
            .Field("sections", "blocks"));

        Errors(builder).Select(error => error.Code).ShouldBe(["FCMS1211", "FCMS1211", "FCMS1211"]);
    }

    private sealed class PluginFieldType(string name, FieldSettingDescriptor[] settings, bool acceptsAnySetting = false) : IFieldType
    {
        public string Name { get; } = name;

        public IReadOnlyList<Type> ClrTypes => [typeof(string)];

        public IReadOnlySet<FluentCMS.Content.FilterOperator> FilterOperators => new HashSet<FluentCMS.Content.FilterOperator>();

        public bool Sortable => false;

        public bool Indexable => false;

        public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = settings;

        public bool AcceptsAnySetting { get; } = acceptsAnySetting;

        public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

        public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
        {
            result = value;
            message = null;
            return true;
        }

        public JsonObject OpenApiSchema(SchemaField field) => [];
    }
}
