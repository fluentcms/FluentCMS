using FluentCMS.Core.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public class ContentScopeTests
{
    private static SchemaBuilder Builder() => SampleSchema.NewBuilder();

    [Fact]
    public void ScopeDefaultsToSite()
    {
        var model = SampleSchema.FromClasses().Build();

        model.FindContentType("article")!.Scope.ShouldBe(ContentScope.Site);
        model.FindContentType("site-settings")!.Scope.ShouldBe(ContentScope.Site);
        model.FindComponent("seo")!.Scope.ShouldBe(ContentScope.Site);
        model.FindBlock("hero")!.Scope.ShouldBe(ContentScope.Site);
    }

    [Fact]
    public void CollectionAttributeSetsTheScope()
    {
        Builder().AddContentType<Tag>().Build().FindContentType("tag")!.Scope.ShouldBe(ContentScope.Global);
    }

    [Fact]
    public void SingletonAttributeSetsTheScope()
    {
        Builder().AddContentType<GlobalSettings>().Build().FindContentType("global-settings")!.Scope.ShouldBe(ContentScope.Global);
    }

    [Fact]
    public void BuilderSetsTheScope()
    {
        var model = Builder()
            .AddCollection("term", type => type.Scope(ContentScope.Global))
            .AddSingleton("settings", type => type.Scope(ContentScope.Global))
            .Build();

        model.FindContentType("term")!.Scope.ShouldBe(ContentScope.Global);
        model.FindContentType("settings")!.Scope.ShouldBe(ContentScope.Global);
    }

    [Fact]
    public void ComponentOrBlockCannotHaveAScope()
    {
        Should.Throw<ArgumentException>(() => Builder().AddComponent("seo", type => type.Scope(ContentScope.Global)));
        Should.Throw<ArgumentException>(() => Builder().AddBlock("hero", type => type.Scope(ContentScope.Global)));

        Builder().AddComponent("seo", type => type.Scope(ContentScope.Site)).Build().FindComponent("seo")!.Scope.ShouldBe(ContentScope.Site);
    }

    [Fact]
    public void SchemaJsonReadsTheScope()
    {
        var model = Builder().AddSchemaJson("""
            {
              "collections": [ { "name": "term", "scope": "global" }, { "name": "page", "scope": "site" } ],
              "singletons": [ { "name": "settings", "scope": "global" } ]
            }
            """).Build();

        model.FindContentType("term")!.Scope.ShouldBe(ContentScope.Global);
        model.FindContentType("page")!.Scope.ShouldBe(ContentScope.Site);
        model.FindContentType("settings")!.Scope.ShouldBe(ContentScope.Global);
    }

    [Fact]
    public void SchemaJsonWritesOnlyGlobal()
    {
        var model = Builder()
            .AddCollection("term", type => type.Scope(ContentScope.Global))
            .AddCollection("page", _ => { })
            .Build();

        var json = SchemaJsonWriter.Write(model);

        json.ShouldContain("\"scope\": \"global\"");
        json.ShouldNotContain("\"scope\": \"site\"");
        SchemaJsonWriter.Write(Builder().AddSchemaJson(json).Build()).ShouldBe(json);
    }

    [Theory]
    [InlineData("""{ "collections": [ { "name": "term", "scope": "tenant" } ] }""", "must be")]
    [InlineData("""{ "collections": [ { "name": "term", "scope": 1 } ] }""", "must be")]
    [InlineData("""{ "components": [ { "name": "seo", "scope": "global" } ] }""", "only allowed")]
    [InlineData("""{ "blocks": [ { "name": "hero", "scope": "site" } ] }""", "only allowed")]
    public void SchemaJsonRejectsABadScope(string json, string message)
    {
        var exception = Should.Throw<SchemaValidationException>(() => Builder().AddSchemaJson(json).Build());

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(SchemaErrorCodes.InvalidSchemaFile);
        exception.Message.ShouldContain(message);
    }

    [Singleton("global-settings", Scope = ContentScope.Global)]
    private sealed class GlobalSettings : ContentItem
    {
    }
}
