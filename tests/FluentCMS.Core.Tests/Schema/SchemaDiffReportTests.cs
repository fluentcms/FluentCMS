using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

// What `schema diff` and `migrations add` print: the drift log's changes, the destructive ones marked, and possible renames.
public class SchemaDiffReportTests
{
    private static readonly string[] Options = ["a", "b"];

    private static SchemaModel Article(Action<ITypeBuilder> fields, bool localized = false) =>
        SampleSchema.NewBuilder().AddCollection("article", type =>
        {
            type.Localized(localized);
            fields(type);
        }).Build();

    private static SchemaDiffReport Report(SchemaModel before, SchemaModel after) =>
        SchemaSnapshotDiff.Report(SchemaSnapshot.Of(before).Json, SchemaSnapshot.Of(after).Json);

    private static IReadOnlyList<string> Destructive(SchemaModel before, SchemaModel after) =>
        [.. Report(before, after).Destructive.Select(change => change.ToString())];

    [Fact]
    public void ARemovedTypeIsDestructive()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text"))
            .AddCollection("page", type => type.Field("title", "text"))
            .Build();

        Destructive(before, Article(type => type.Field("title", "text"))).ShouldBe(["type 'page' removed"]);
    }

    [Fact]
    public void ARemovedFieldIsDestructive()
    {
        var before = Article(type => type.Field("title", "text").Field("body", "longText"));

        Destructive(before, Article(type => type.Field("title", "text"))).ShouldBe(["field 'article.body' removed"]);
    }

    [Fact]
    public void AChangedKindIsDestructive()
    {
        var before = Article(type => type.Field("title", "text").Field("rank", "text").Field("tags", "select", field => field.Setting("options", Options)));
        var after = Article(type => type.Field("title", "text").Field("rank", "integer").Field("tags", "select", field => field.Multiple().Setting("options", Options)));

        Destructive(before, after).ShouldBe(["field 'article.rank' changed (type)", "field 'article.tags' changed (multiple)"]);
    }

    [Fact]
    public void AFieldThatMovesToTheTableOfALocalizedTypeIsDestructive()
    {
        var before = Article(type => type.Field("title", "text"), localized: true);
        var after = Article(type => type.Field("title", "text", field => field.Localizable()), localized: true);

        Destructive(before, after).ShouldBe(["field 'article.title' changed (localizable)"]);
    }

    [Fact]
    public void WhatKeepsTheStoredValuesIsNotDestructive()
    {
        var before = Article(type => type.Field("title", "text"));
        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text", field => field.Required().Length(null, 80)).Field("summary", "longText"))
            .AddCollection("page", type => type.Field("title", "text"))
            .Build();

        var report = Report(before, after);

        report.Changes.Count.ShouldBe(3);
        report.Destructive.ShouldBeEmpty();
        report.Summary.ShouldBe("3 differences");
    }

    [Fact]
    public void AFieldRenamedWithoutFormerNamesIsAPossibleRenameWithTheAttributeToAdd()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "text").Field("summary", "text"));

        var report = Report(before, after);

        report.Changes.Select(change => change.ToString()).ShouldBe(["field 'article.summary' added", "field 'article.subtitle' removed"], ignoreOrder: true);
        report.Destructive.Select(change => change.ToString()).ShouldBe(["field 'article.subtitle' removed"]);
        var rename = report.PossibleRenames.ShouldHaveSingleItem();
        rename.Attribute.ShouldBe("[FormerNames(\"subtitle\")]");
        rename.ToString().ShouldBe("possible rename: field 'article.subtitle' to 'summary'; add [FormerNames(\"subtitle\")] to 'summary' to make it one");
    }

    [Fact]
    public void WithTheAttributeItIsARenameAndNothingIsLost()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "text").Field("summary", "text", field => field.FormerNames("subtitle")));

        var report = Report(before, after);

        report.Changes.Select(change => change.ToString()).ShouldBe(["field 'article.subtitle' renamed to 'summary'"]);
        report.Destructive.ShouldBeEmpty();
        report.PossibleRenames.ShouldBeEmpty();
    }

    [Fact]
    public void AFieldOfAnotherKindIsNoPossibleRename()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "text").Field("rank", "integer"));

        Report(before, after).PossibleRenames.ShouldBeEmpty();
    }

    [Fact]
    public void EachRemovedFieldIsSuggestedForOneAddedFieldInOrder()
    {
        var before = Article(type => type.Field("a", "text").Field("b", "text"));
        var after = Article(type => type.Field("c", "text").Field("d", "text").Field("e", "text"));

        Report(before, after).PossibleRenames.Select(rename => $"{rename.From}>{rename.To}").ShouldBe(["a>c", "b>d"]);
    }

    [Fact]
    public async Task TheTextMarksDestructiveChangesAndEndsWithTheRenames()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "integer").Field("summary", "text"));

        using var text = new StringWriter();
        await Report(before, after).WriteTextAsync(text);

        text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "field 'article.title' changed (type) [destructive]",
            "field 'article.summary' added",
            "field 'article.subtitle' removed [destructive]",
            "possible rename: field 'article.subtitle' to 'summary'; add [FormerNames(\"subtitle\")] to 'summary' to make it one",
        ]);
    }

    [Fact]
    public void TheJsonIsOneLineWithEveryChangeAndRename()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "text").Field("summary", "text"));

        var json = Report(before, after).ToJson();

        json.ShouldNotContain("\n");
        var root = JsonDocument.Parse(json).RootElement;
        var removed = root.GetProperty("changes").EnumerateArray().Single(change => change.GetProperty("kind").GetString() == "fieldRemoved");
        removed.GetProperty("field").GetString().ShouldBe("subtitle");
        removed.GetProperty("destructive").GetBoolean().ShouldBeTrue();
        removed.GetProperty("text").GetString().ShouldBe("field 'article.subtitle' removed");
        root.GetProperty("possibleRenames")[0].GetProperty("attribute").GetString().ShouldBe("[FormerNames(\"subtitle\")]");
    }

    [Fact]
    public async Task NoChangeSaysSo()
    {
        var schema = Article(type => type.Field("title", "text"));

        var report = Report(schema, schema);

        report.HasChanges.ShouldBeFalse();
        using var text = new StringWriter();
        await report.WriteTextAsync(text);
        text.ToString().Trim().ShouldBe("No difference.");
    }

    [Fact]
    public void ASchemaFileWithAFieldTypeOfAPluginIsReadWithoutTheHost()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text" }, { "name": "hero", "type": "media", "multiple": true } ] } ] }""");
            var fieldTypes = new UncheckedFieldTypes();

            var schema = SchemaFiles.Read(file, fieldTypes);

            schema.ContentTypes.ShouldHaveSingleItem().Fields.Select(field => field.FieldType).ShouldBe(["text", "media"]);
            fieldTypes.Unchecked.ShouldBe(["media"]);
            Should.Throw<SchemaValidationException>(() => SchemaFiles.Read(file, Core.Schema.FieldTypes.FieldTypeRegistry.Core))
                .Errors.ShouldContain(error => error.Code == "FCMS1203");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
