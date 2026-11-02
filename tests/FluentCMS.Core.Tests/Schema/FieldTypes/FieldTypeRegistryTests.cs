using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class FieldTypeRegistryTests
{
    private static readonly string[] CoreNames =
    [
        "blocks", "boolean", "component", "date", "dateTime", "decimal", "email", "integer", "json", "longText", "relation", "richText", "select", "slug", "text", "url",
    ];

    [Fact]
    public void CoreListsTheSixteenCoreTypesOrderedByName()
    {
        FieldTypeRegistry.Core.All.Select(type => type.Name).ShouldBe(CoreNames);
    }

    [Fact]
    public void FindReturnsTheDescriptorOfAName()
    {
        foreach (var name in CoreNames)
        {
            FieldTypeRegistry.Core.Find(name).ShouldNotBeNull().Name.ShouldBe(name);
        }
    }

    [Theory]
    [InlineData("media")]
    [InlineData("Text")]
    [InlineData("")]
    public void FindReturnsNullForAnUnknownName(string name)
    {
        FieldTypeRegistry.Core.Find(name).ShouldBeNull();
    }

    [Fact]
    public void ExtrasJoinTheCoreTypesInNameOrder()
    {
        var media = new MediaFieldType();

        var registry = new FieldTypeRegistry([media]);

        registry.All.Count.ShouldBe(17);
        registry.All.Select(type => type.Name).ShouldBe([.. CoreNames.Append("media").Order(StringComparer.Ordinal)]);
        registry.Find("media").ShouldBeSameAs(media);
        FieldTypeRegistry.Core.All.Count.ShouldBe(16);
    }

    [Fact]
    public void ADuplicateExtraNameIsRejected()
    {
        var exception = Should.Throw<ArgumentException>(() => new FieldTypeRegistry([new MediaFieldType(), new MediaFieldType()]));

        exception.Message.ShouldContain("'media'");
    }

    [Fact]
    public void AnExtraCannotReplaceACoreType()
    {
        var exception = Should.Throw<ArgumentException>(() => new FieldTypeRegistry([new NamedFieldType("text")]));

        exception.Message.ShouldContain("'text'");
    }

    [Fact]
    public void ANullExtraIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => new FieldTypeRegistry(null!));
        Should.Throw<ArgumentNullException>(() => new FieldTypeRegistry([null!]));
    }

    [Fact]
    public void AMediaFieldBuildsWhenTheMediaTypeIsRegistered()
    {
        var registry = new FieldTypeRegistry([new MediaFieldType()]);

        var model = new SchemaBuilder(registry)
            .AddContentType<Article>()
            .AddContentType<Author>()
            .AddContentType<Tag>()
            .AddContentType<Seo>()
            .AddContentType<HeroBlock>()
            .Build();

        var hero = model.FindContentType("article")!.Fields.Single(field => field.Name == "hero");
        hero.FieldType.ShouldBe("media");
        registry.Find(hero.FieldType)!.StorageKind(hero).ShouldBe(FieldStorageKind.Reference);
    }

    [Fact]
    public void AMediaFieldFailsWithoutTheMediaType()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("hero", "media"));

        var error = Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.ShouldHaveSingleItem();

        error.ToString().ShouldBe("FCMS1203: field type 'media' used by article.hero is not registered");
    }

    [Fact]
    public void ABuilderWithoutARegistryUsesTheCoreTypes()
    {
        var model = new SchemaBuilder().AddCollection("article", type => type.Field("title", "text")).Build();

        model.FindContentType("article").ShouldNotBeNull();
    }

    [Fact]
    public void ABuilderRequiresARegistry()
    {
        Should.Throw<ArgumentNullException>(() => new SchemaBuilder(null!));
    }

    private sealed class NamedFieldType(string name) : IFieldType
    {
        public string Name { get; } = name;

        public IReadOnlyList<Type> ClrTypes => [];

        public IReadOnlySet<FluentCMS.Content.FilterOperator> FilterOperators => new HashSet<FluentCMS.Content.FilterOperator>();

        public bool Sortable => false;

        public bool Indexable => false;

        public IReadOnlyList<FieldSettingDescriptor> Settings => [];

        public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

        public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
        {
            result = value;
            message = null;
            return true;
        }

        public System.Text.Json.Nodes.JsonObject OpenApiSchema(SchemaField field) => [];
    }
}
