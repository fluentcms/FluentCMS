using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class ModelCacheTests
{
    private static SchemaModel PageSchema(string field) =>
        SampleSchema.NewBuilder().AddCollection("page", type => type.Field(field, "text")).Build();

    private static IModel ModelOf(SchemaModel schema, IStorageConventions? conventions = null)
    {
        using var database = new StorageDatabase(schema, conventions);
        using var context = database.CreateContext();
        return context.Model;
    }

    [Fact]
    public void ContextsOverTheSameSchemaShareAModel()
    {
        var schema = PageSchema("cacheSame");

        ModelOf(schema).ShouldBeSameAs(ModelOf(schema));
    }

    [Fact]
    public void EqualSchemasShareAModel()
    {
        ModelOf(PageSchema("cacheEqual")).ShouldBeSameAs(ModelOf(PageSchema("cacheEqual")));
    }

    [Fact]
    public void DifferentSchemasGetDifferentModels()
    {
        var first = ModelOf(PageSchema("cacheFirst"));
        var second = ModelOf(PageSchema("cacheSecond"));

        first.ShouldNotBeSameAs(second);
        first.FindEntityType("cms_page")!.FindProperty("cache_first").ShouldNotBeNull();
        second.FindEntityType("cms_page")!.FindProperty("cache_first").ShouldBeNull();
    }

    [Fact]
    public void ScopeChangesTheModel()
    {
        var site = SampleSchema.NewBuilder().AddCollection("scoped", type => type.Field("name", "text")).Build();
        var global = SampleSchema.NewBuilder().AddCollection("scoped", type => type.Scope(ContentScope.Global).Field("name", "text")).Build();

        ModelOf(site).FindEntityType("cms_scoped")!.FindProperty("site_id")!.IsNullable.ShouldBeFalse();
        ModelOf(global).FindEntityType("cms_scoped")!.FindProperty("site_id")!.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void ConventionsChangeTheModel()
    {
        var schema = PageSchema("cacheConventions");

        ModelOf(schema).ShouldNotBeSameAs(ModelOf(schema, new SqliteLikeConventions()));
    }
}
