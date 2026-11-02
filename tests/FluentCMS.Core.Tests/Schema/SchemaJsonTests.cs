using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public class SchemaJsonTests
{
    private static SchemaBuilder Builder() => new();

    private static IReadOnlyList<SchemaError> Errors(string json)
    {
        var builder = Builder().AddSchemaJson(json);
        return Should.Throw<SchemaValidationException>(() => builder.Build()).Errors;
    }

    private static void ShouldReportOnly(string json, string messagePart)
    {
        var errors = Errors(json);

        errors.Select(error => error.Code).ShouldBe(["FCMS1213"]);
        errors[0].Message.ShouldContain(messagePart);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        const string json = """
            {
              // a comment
              "collections": [
                { "name": "article", "fields": [ { "name": "title", "type": "text", }, ], },
              ],
            }
            """;

        Builder().AddSchemaJson(json).Build().FindContentType("article")!.FindField("title").ShouldNotBeNull();
    }

    [Fact]
    public void FieldsAreOptional()
    {
        Builder().AddSchemaJson("""{ "collections": [ { "name": "article" } ] }""").Build()
            .FindContentType("article")!.Fields.ShouldBeEmpty();
    }

    [Fact]
    public void EverythingTheModelHoldsSurvivesTheCanonicalForm()
    {
        const string json = """
            {
              "collections": [
                {
                  "name": "article",
                  "localized": true,
                  "fields": [
                    { "name": "category", "type": "select", "options": ["news", "video"] },
                    { "name": "rating", "type": "decimal", "minimum": 0, "maximum": 5.5, "precision": 4, "scale": 1, "defaultValue": 1.5 },
                    { "name": "tags", "type": "select", "multiple": true, "options": ["a", "b"], "defaultValue": ["a"] },
                    {
                      "name": "code",
                      "type": "text",
                      "minLength": 2,
                      "maxLength": 8,
                      "pattern": "^[A-Z]+$",
                      "required": true,
                      "unique": true,
                      "indexed": true,
                      "searchable": true,
                      "readOnly": true,
                      "hidden": true,
                      "localizable": true,
                      "formerNames": ["sku"],
                      "defaultValue": "AB",
                      "display": { "label": "Code", "help": "Help", "placeholder": "AB", "group": "Main", "tab": "Info", "width": 6 },
                      "visibleWhen": { "and": [ { "field": "category", "in": ["news", "video"] }, { "not": { "field": "rating", "isEmpty": true } } ] },
                      "requiredWhen": { "or": [ { "field": "category", "equals": "video" }, { "field": "rating", "equals": 3 } ] }
                    }
                  ]
                }
              ]
            }
            """;

        var model = Builder().AddSchemaJson(json).Build();
        var canonical = SchemaJsonWriter.Write(model);

        SchemaJsonWriter.Write(Builder().AddSchemaJson(canonical).Build()).ShouldBe(canonical);

        var code = model.FindContentType("article")!.FindField("code")!;
        code.Display.ShouldNotBeNull().Width.ShouldBe(6);
        var visibleWhen = code.VisibleWhen.ShouldNotBeNull();
        visibleWhen.Operator.ShouldBe(ConditionOperator.And);
        visibleWhen.Operands.Count.ShouldBe(2);
        code.RequiredWhen.ShouldNotBeNull().ToString().ShouldBe("(category == \"video\") or (rating == 3)");
        model.FindContentType("article")!.FindField("rating")!.DefaultValue.ShouldBe(1.5m);
    }

    [Fact]
    public void UnknownFieldPropertiesBecomeSettingsOfTheFieldType()
    {
        const string json = """
            { "collections": [ { "name": "article", "fields": [
              { "name": "hero", "type": "text", "accepts": ["image", "video"], "maxSize": 5, "inline": true, "mode": "wide" }
            ] } ] }
            """;

        var settings = Builder().AddSchemaJson(json).Build().FindContentType("article")!.FindField("hero")!.Settings;

        settings.Keys.ShouldBe(["accepts", "inline", "maxSize", "mode"]);
        ((IEnumerable<string>)settings["accepts"]).ShouldBe(["image", "video"]);
        settings["maxSize"].ShouldBe(5L);
        settings["inline"].ShouldBe(true);
        settings["mode"].ShouldBe("wide");
    }

    [Fact]
    public void SeveralDocumentsAddUp()
    {
        var builder = Builder()
            .AddSchemaJson("""{ "collections": [ { "name": "article" } ] }""")
            .AddSchemaJson("""{ "collections": [ { "name": "author" } ], "blocks": [ { "name": "hero" } ] }""");

        var model = builder.Build();

        model.ContentTypes.Select(type => type.Name).ShouldBe(["article", "author"]);
        model.Blocks.Select(type => type.Name).ShouldBe(["hero"]);
    }

    [Fact]
    public void SameTypeInTwoDocumentsIsFCMS1201()
    {
        var builder = Builder()
            .AddSchemaJson("""{ "collections": [ { "name": "article" } ] }""")
            .AddSchemaJson("""{ "collections": [ { "name": "article" } ] }""");

        Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.Select(error => error.Code).ShouldBe(["FCMS1201"]);
    }

    [Fact]
    public void SchemaFileIsReadFromThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluentcms-{Guid.NewGuid():N}.schema.json");
        File.WriteAllText(path, """{ "collections": [ { "name": "article" } ] }""");

        try
        {
            Builder().LoadSchemaFile(path).Build().FindContentType("article").ShouldNotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ErrorsInAFileNameTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluentcms-{Guid.NewGuid():N}.schema.json");
        File.WriteAllText(path, """{ "collections": 3 }""");

        try
        {
            var builder = Builder().LoadSchemaFile(path);

            Should.Throw<SchemaValidationException>(() => builder.Build()).Errors[0].Message.ShouldStartWith($"{path}: collections must be an array");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingSchemaFileThrowsFileNotFound()
    {
        Should.Throw<FileNotFoundException>(() => Builder().LoadSchemaFile(Path.Combine(Path.GetTempPath(), "does-not-exist.schema.json")));
    }

    [Fact]
    public void TextThatIsNotJsonIsFCMS1213()
    {
        ShouldReportOnly("{ not json", "schema.json is not valid JSON");
    }

    [Fact]
    public void DocumentThatIsNotAnObjectIsFCMS1213()
    {
        ShouldReportOnly("[]", "schema.json must be a JSON object");
    }

    [Fact]
    public void UnknownTopLevelPropertyIsFCMS1213()
    {
        ShouldReportOnly("""{ "tables": [] }""", "schema.json: tables is not a known property");
    }

    [Fact]
    public void SchemaPropertyIsAllowedAtTheTop()
    {
        Builder().AddSchemaJson("""{ "$schema": "https://example.com/schema.json" }""").Build().ContentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void TypeWithoutANameIsFCMS1213()
    {
        ShouldReportOnly("""{ "collections": [ { "fields": [] } ] }""", "collections[0] needs a name");
    }

    [Fact]
    public void UnknownTypePropertyIsFCMS1213()
    {
        ShouldReportOnly("""{ "collections": [ { "name": "article", "label": "Article" } ] }""", "collections[0].label is not a known property of a type");
    }

    [Fact]
    public void FieldWithoutATypeIsFCMS1213()
    {
        ShouldReportOnly("""{ "collections": [ { "name": "article", "fields": [ { "name": "title" } ] } ] }""", "collections[0].fields[0] needs a name and a type");
    }

    [Fact]
    public void FieldFlagThatIsNotABooleanIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text", "required": "yes" } ] } ] }""",
            "collections[0].fields[0].required must be true or false");
    }

    [Fact]
    public void LimitThatIsNotANumberIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text", "maxLength": "ten" } ] } ] }""",
            "collections[0].fields[0].maxLength must be a whole number");
    }

    [Fact]
    public void SettingThatIsAnObjectIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text", "extra": { "a": 1 } } ] } ] }""",
            "collections[0].fields[0].extra");
    }

    [Fact]
    public void ReverseWithoutATargetIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "author", "type": "relation", "reverse": "articles" } ] } ] }""",
            "sets reverse or ordered without a target");
    }

    [Theory]
    [InlineData("""{ "field": "a" }""", "needs exactly one of")]
    [InlineData("""{ "field": "a", "equals": 1, "isEmpty": true }""", "needs exactly one of")]
    [InlineData("""{ "equals": 1 }""", "needs a field")]
    [InlineData("""{ "field": "a", "isEmpty": false }""", "isEmpty must be true")]
    [InlineData("""{ "field": "a", "in": 3 }""", "in must be an array")]
    [InlineData("""{ "and": [] }""", "must be a non-empty array of conditions")]
    [InlineData("""{ "field": "a", "contains": 1 }""", "is not a known condition property")]
    [InlineData("""[]""", "must be an object")]
    public void MalformedConditionIsFCMS1213(string condition, string messagePart)
    {
        ShouldReportOnly(
            $$"""{ "collections": [ { "name": "article", "fields": [ { "name": "a", "type": "text" }, { "name": "b", "type": "text", "visibleWhen": {{condition}} } ] } ] }""",
            messagePart);
    }

    [Fact]
    public void ListAsEqualsValueIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "category", "type": "text" }, { "name": "videoUrl", "type": "url", "visibleWhen": { "field": "category", "equals": ["video"] } } ] } ] }""",
            "collections[0].fields[1].visibleWhen.equals must be a string, boolean, number or null");
    }

    [Fact]
    public void ListAsMemberOfInIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "category", "type": "text" }, { "name": "videoUrl", "type": "url", "visibleWhen": { "field": "category", "in": ["video", ["audio"]] } } ] } ] }""",
            "collections[0].fields[1].visibleWhen.in[1] must be a string, boolean, number or null");
    }

    [Fact]
    public void ObjectAsMemberOfInIsFCMS1213()
    {
        ShouldReportOnly(
            """{ "collections": [ { "name": "article", "fields": [ { "name": "category", "type": "text" }, { "name": "videoUrl", "type": "url", "visibleWhen": { "field": "category", "in": [{}] } } ] } ] }""",
            "collections[0].fields[1].visibleWhen.in[0] must be a string, boolean, number or array of strings");
    }

    [Fact]
    public void ReverseOnAComponentFieldIsFCMS1217()
    {
        const string json = """
            {
              "collections": [ { "name": "tag" } ],
              "components": [ { "name": "seo", "fields": [ { "name": "tag", "type": "relation", "target": "tag", "reverse": "pages" } ] } ]
            }
            """;

        var errors = Errors(json);

        errors.Select(error => error.Code).ShouldBe(["FCMS1217"]);
        errors[0].Message.ShouldContain("relation seo.tag declares a reverse side");
    }

    [Fact]
    public void ReverseOnABlockFieldIsFCMS1217()
    {
        const string json = """
            {
              "collections": [ { "name": "tag" } ],
              "blocks": [ { "name": "hero", "fields": [ { "name": "tag", "type": "relation", "target": "tag", "reverse": "pages" } ] } ]
            }
            """;

        var errors = Errors(json);

        errors.Select(error => error.Code).ShouldBe(["FCMS1217"]);
        errors[0].Message.ShouldContain("not of a block");
    }

    [Fact]
    public void RelationWithoutReverseIsAllowedInAComponent()
    {
        const string json = """
            {
              "collections": [ { "name": "tag" } ],
              "components": [ { "name": "seo", "fields": [ { "name": "tag", "type": "relation", "target": "tag" } ] } ]
            }
            """;

        Builder().AddSchemaJson(json).Build().FindContentType("tag")!.ReverseRelations.ShouldBeEmpty();
    }

    [Fact]
    public void ProblemsAreReportedTogetherAndTheRestOfTheDocumentIsStillRead()
    {
        const string json = """
            {
              "collections": [
                { "name": "article", "fields": [ { "name": "title", "type": "text", "required": 1 } ] },
                { "name": "author", "fields": [ { "name": "name" } ] },
                { "name": "tag", "fields": [ { "name": "name", "type": "mystery" } ] }
              ]
            }
            """;

        var errors = Errors(json);

        errors.Select(error => error.Code).ShouldBe(["FCMS1213", "FCMS1213", "FCMS1203"]);
    }
}
