using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public class SchemaSnapshotTests
{
    private static SchemaModel Article(Action<ITypeBuilder> fields) => SampleSchema.NewBuilder().AddCollection("article", fields).Build();

    private static IReadOnlyList<string> Diff(SchemaModel before, SchemaModel after) =>
        [.. SchemaSnapshotDiff.Compare(SchemaSnapshot.Of(before).Json, SchemaSnapshot.Of(after).Json).Select(change => change.ToString())];

    [Fact]
    public void TwoBuildsOfOneSchemaGiveIdenticalBytes()
    {
        var first = SchemaSnapshot.Of(SampleSchema.FromClasses().Build());
        var second = SchemaSnapshot.Of(SampleSchema.FromClasses().Build());

        second.Json.ShouldBe(first.Json);
        second.Hash.ShouldBe(first.Hash);
        System.Text.Encoding.UTF8.GetBytes(second.Json).ShouldBe(System.Text.Encoding.UTF8.GetBytes(first.Json));
    }

    [Fact]
    public void TheOrderTypesAreAddedInDoesNotMatter()
    {
        var one = SampleSchema.NewBuilder().AddCollection("alpha", type => type.Field("a", "text")).AddCollection("beta", type => type.Field("b", "text")).Build();
        var other = SampleSchema.NewBuilder().AddCollection("beta", type => type.Field("b", "text")).AddCollection("alpha", type => type.Field("a", "text")).Build();

        SchemaSnapshot.Of(other).ShouldBe(SchemaSnapshot.Of(one));
    }

    [Fact]
    public void TheHashIsTheSha256OfTheSnapshotInLowerCaseHex()
    {
        var snapshot = SchemaSnapshot.Of(SampleSchema.FromClasses().Build());

        snapshot.Hash.Length.ShouldBe(64);
        snapshot.Hash.ShouldBe(snapshot.Hash.ToLowerInvariant());
        SchemaSnapshot.HashOf(snapshot.Json).ShouldBe(snapshot.Hash);
    }

    [Fact]
    public void ARuntimeTypeIsNotInTheSnapshot()
    {
        var json = SchemaSnapshot.Of(SampleSchema.FromClasses().Build()).Json;

        json.ShouldNotContain("FluentCMS.Core.Tests");
        json.ShouldNotContain("ClrType");
    }

    [Fact]
    public void AnyChangeToAFieldChangesTheHash()
    {
        var before = SchemaSnapshot.Of(Article(type => type.Field("title", "text")));

        SchemaSnapshot.Of(Article(type => type.Field("title", "text", field => field.Required()))).Hash.ShouldNotBe(before.Hash);
        SchemaSnapshot.Of(Article(type => type.Field("title", "text", field => field.Length(null, 10)))).Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void ARenameWithFormerNamesIsARename()
    {
        var before = Article(type => type.Field("title", "text").Field("subtitle", "text"));
        var after = Article(type => type.Field("title", "text").Field("summary", "text", field => field.FormerNames("subtitle")));

        Diff(before, after).ShouldBe(["field 'article.subtitle' renamed to 'summary'"]);
    }

    [Fact]
    public void ARenameWithoutFormerNamesIsADropAndAnAdd()
    {
        var before = Article(type => type.Field("subtitle", "text"));
        var after = Article(type => type.Field("summary", "text"));

        Diff(before, after).ShouldBe(["field 'article.summary' added", "field 'article.subtitle' removed"], ignoreOrder: true);
    }

    [Fact]
    public void ARenameThatAlsoChangesTheFieldSaysBoth()
    {
        var before = Article(type => type.Field("subtitle", "text"));
        var after = Article(type => type.Field("summary", "text", field => field.FormerNames("subtitle").Length(null, 20)));

        Diff(before, after).ShouldBe(["field 'article.subtitle' renamed to 'summary'", "field 'article.summary' changed (maxLength)"]);
    }

    [Fact]
    public void AFormerNameThatIsStillAFieldIsNotARename()
    {
        var before = Article(type => type.Field("subtitle", "text"));
        var after = Article(type => type.Field("subtitle", "text").Field("summary", "text", field => field.FormerNames("subtitle")));

        Diff(before, after).ShouldBe(["field 'article.summary' added"]);
    }

    [Fact]
    public void ChangesNameTheTypesAndFieldsAndNeverTheValues()
    {
        var before = Article(type => type.Field("title", "text").Field("body", "longText"));
        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text", field => field.Required().DefaultValue("secret-default")).Field("body", "longText"))
            .AddSingleton("settings", type => type.Field("name", "text"))
            .Build();

        var changes = Diff(before, after);

        changes.ShouldBe(["field 'article.title' changed (defaultValue, required)", "type 'settings' added"]);
        string.Concat(changes).ShouldNotContain("secret-default");
        Diff(after, before).ShouldBe(["field 'article.title' changed (defaultValue, required)", "type 'settings' removed"], ignoreOrder: true);
    }

    [Fact]
    public void ATypeThatChangesKindIsAChangedType()
    {
        var collection = SampleSchema.NewBuilder().AddCollection("home", type => type.Field("title", "text")).Build();
        var singleton = SampleSchema.NewBuilder().AddSingleton("home", type => type.Field("title", "text")).Build();

        Diff(collection, singleton).ShouldBe(["type 'home' changed (kind)"]);
    }

    [Fact]
    public void ReorderingFieldsIsReported()
    {
        var before = Article(type => type.Field("title", "text").Field("body", "longText"));
        var after = Article(type => type.Field("body", "longText").Field("title", "text"));

        Diff(before, after).ShouldBe(["fields of type 'article' reordered"]);
    }

    [Fact]
    public void TheSameSchemaHasNoChanges()
    {
        var schema = SampleSchema.FromClasses().Build();

        Diff(schema, SampleSchema.FromClasses().Build()).ShouldBeEmpty();
    }
}
