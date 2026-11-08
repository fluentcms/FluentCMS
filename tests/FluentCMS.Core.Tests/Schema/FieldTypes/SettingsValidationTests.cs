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
    private static readonly string[] Tones = ["calm", "elevenchars"];

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

        var error = Errors(builder).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("setting 'options' of select field article.category must be a list of strings");
    }

    [Fact]
    public void ASelectOptionLongerThanItsColumnIsRefused()
    {
        var longer = new string('x', 65);
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("category", "select", field => field.Setting(FieldSettings.Options, new[] { "news", longer }))
            .Field("tone", "select", field => field.Length(null, 10).Setting(FieldSettings.Options, Tones)));

        Errors(builder).Select(error => (error.Code, error.Message)).ShouldBe(
        [
            ("FCMS1211", $"option '{longer}' of select field article.category has 65 characters; an option has at most 64, unless the field sets a larger maximum length"),
            ("FCMS1211", "option 'elevenchars' of select field article.tone has 11 characters; an option has at most the field's maximum length of 10"),
        ]);
    }

    [Fact]
    public void ALongerOptionNeedsAMaximumLengthOrAMultipleSelect()
    {
        var longer = new string('x', 80);

        Should.NotThrow(() => new SchemaBuilder().AddCollection("article", type => type
            .Field("category", "select", field => field.Length(null, 80).Setting(FieldSettings.Options, new[] { longer }))
            .Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, new[] { longer })))
            .Build());
    }

    [Fact]
    public void AComponentOrBlocksSettingOfTheWrongKindIsReportedOnce()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, 5))
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, "hero")));

        var errors = Errors(builder);

        errors.Select(error => error.Message).ShouldBe(
        [
            "setting 'component' of component field article.seo must be a string",
            "setting 'blocks' of blocks field article.sections must be a list of strings",
        ]);
    }

    [Theory]
    [InlineData(0, null, "precision of 0; it must be between 1 and 28")]
    [InlineData(-1, null, "precision of -1; it must be between 1 and 28")]
    [InlineData(29, null, "precision of 29; it must be between 1 and 28")]
    [InlineData(null, -1, "scale of -1; it must be between 0 and 28")]
    [InlineData(null, 29, "scale of 29; it must be between 0 and 28")]
    [InlineData(2, 3, "scale of 3 above its precision of 2")]
    public void DecimalPrecisionAndScaleOutOfRangeAreReported(int? precision, int? scale, string messagePart)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("price", "decimal", field => Decimal(field, precision, scale)));

        var error = Errors(builder).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe($"decimal field article.price has a {messagePart}");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 2)]
    [InlineData(5, 5)]
    [InlineData(28, 28)]
    [InlineData(28, null)]
    [InlineData(null, 3)]
    [InlineData(null, 0)]
    public void DecimalPrecisionAndScaleInRangeBuild(int? precision, int? scale)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("price", "decimal", field => Decimal(field, precision, scale)));

        builder.Build().FindContentType("article").ShouldNotBeNull();
    }

    private static void Decimal(IFieldBuilder field, int? precision, int? scale)
    {
        if (precision is { } digits)
        {
            field.Setting(FieldSettings.Precision, digits);
        }

        if (scale is { } places)
        {
            field.Setting(FieldSettings.Scale, places);
        }
    }

    [Fact]
    public void AMissingRequiredKeyIsReportedWithTheTypeFieldAndKey()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor { Key = "provider", Kind = FieldSettingKind.String, Required = true }])]);
        var builder = new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map"));

        var error = Errors(builder).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("map field place.location is missing the required setting 'provider'");
    }

    [Fact]
    public void ARequiredKeyThatIsSetBuilds()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor { Key = "provider", Kind = FieldSettingKind.String, Required = true }, new FieldSettingDescriptor { Key = "zoom", Kind = FieldSettingKind.Integer }])]);
        var builder = new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map", field => field.Setting("provider", "osm").Setting("zoom", 12)));

        builder.Build().FindContentType("place")!.Fields.Single().Settings["provider"].ShouldBe("osm");
    }

    [Fact]
    public void AKindOfTheDecimalSettingAcceptsWholeAndFractionalNumbers()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("ratio", [new FieldSettingDescriptor { Key = "step", Kind = FieldSettingKind.Decimal }, new FieldSettingDescriptor { Key = "live", Kind = FieldSettingKind.Boolean }])]);

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
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor { Key = "provider", Kind = FieldSettingKind.String, Required = true }], acceptsAnySetting: true)]);

        Errors(new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map", field => field.Setting("zoom", 12))))
            .ShouldHaveSingleItem().Message.ShouldContain("missing the required setting 'provider'");
    }

    [Fact]
    public void ATypeThatAcceptsAnySettingStillChecksTheKindOfTheDeclaredOnes()
    {
        var registry = new FieldTypeRegistry([new PluginFieldType("map", [new FieldSettingDescriptor { Key = "provider", Kind = FieldSettingKind.String, Required = true }], acceptsAnySetting: true)]);
        var builder = new SchemaBuilder(registry).AddCollection("place", type => type.Field("location", "map", field => field.Setting("provider", 5).Setting("zoom", 12)));

        var error = Errors(builder).ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("setting 'provider' of map field place.location must be a string");
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

        public bool Sortable(SchemaField field) => false;

        public bool Indexable(SchemaField field) => false;

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
