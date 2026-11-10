using System.Text.Json.Nodes;
using FluentCMS.Schema;
using HotChocolate.Types;

namespace FluentCMS.Plugins.GraphQL.Tests;

[Collection("note")]
public sealed class Note : ContentItem
{
    [Text, Required]
    public string Title { get; set; } = "";
}

// The shapes the Blog sample does not have: a singleton, numbers, a date, JSON, a repeated select and a repeated component
// with a relation inside, and fields a client does not see or does not write.
[Singleton("site-settings")]
public sealed class SiteSettings : ContentItem
{
    [Text]
    public string? Tagline { get; set; }

    [Integer]
    public long? Visitors { get; set; }

    [Decimal(Precision = 10, Scale = 2)]
    public decimal? Rating { get; set; }

    [Boolean]
    public bool Live { get; set; }

    [Date]
    public DateOnly? Founded { get; set; }

    [Json]
    public JsonNode? Theme { get; set; }

    [Select("web", "print")]
    public List<string> Channels { get; set; } = [];

    // Two options that would be one enum value, so the field stays a string.
    [Select("a-b", "a_b")]
    public string? Variant { get; set; }

    [Component]
    public List<Link> Links { get; set; } = [];

    [Text, Hidden]
    public string? Secret { get; set; }

    [Text, ReadOnly]
    public string? Code { get; set; }
}

[Component]
public sealed class Link
{
    [Text]
    public string? Label { get; set; }

    [Relation]
    public Ref<Note> Target { get; set; }
}

// Two content types whose GraphQL names collide: the list of thing, and the type thing-list.
[Collection("thing")]
public sealed class Thing : ContentItem
{
    [Text]
    public string? Name { get; set; }
}

[Collection("thing-list")]
public sealed class ThingList : ContentItem
{
    [Text]
    public string? Name { get; set; }
}

// A field that never answers, for the execution timeout.
public sealed class SlowContributor : IGraphQLSchemaContributor
{
    public void Configure(HotChocolate.ISchemaBuilder schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        schema.AddType(new ObjectTypeExtension(query =>
        {
            query.Name("Query");
            query.Field("never").Type<BooleanType>().Resolve(async context =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                return (bool?)null;
            });
        }));
    }
}

public sealed record Greeting(string Text);

// What a plugin adds with IGraphQLSchemaContributor: a type of its own and a field of Query that returns it.
public sealed class GreetingContributor : IGraphQLSchemaContributor
{
    public void Configure(HotChocolate.ISchemaBuilder schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        schema.AddType(new ObjectType<Greeting>(greeting => greeting.Name("Greeting")));
        schema.AddType(new ObjectTypeExtension(query =>
        {
            query.Name("Query");
            query.Field("greeting")
                .Argument("name", argument => argument.Type<NonNullType<StringType>>())
                .Type<ObjectType<Greeting>>()
                .Resolve(context => new Greeting($"Hello, {context.ArgumentValue<string>("name")}."));
        }));
    }
}
