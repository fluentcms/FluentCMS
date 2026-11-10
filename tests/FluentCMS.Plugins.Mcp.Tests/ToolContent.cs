using FluentCMS.Schema;

namespace FluentCMS.Plugins.Mcp.Tests;

// The types the content tools are tested on: a collection with every kind of field a tool schema follows, a second
// collection that a relation points to, and a singleton.
[Collection("post")]
public sealed class Post : ContentItem
{
    [Text(MaxLength = 20), Required, Display(Help = "The headline")]
    public string Title { get; set; } = "";

    [Select("news", "opinion")]
    public string? Category { get; set; }

    [Integer, Range(1, 5)]
    public long? Rating { get; set; }

    [Relation]
    public Ref<Writer>? Author { get; set; }

    [Component]
    public Seo? Seo { get; set; }

    [Blocks(typeof(QuoteBlock))]
    public BlockList Sections { get; set; } = [];

    [Text]
    public string? Note { get; set; }

    [Text, Hidden]
    public string? Internal { get; set; }

    [Text, ReadOnly]
    public string? Imported { get; set; }
}

[Collection("writer")]
public sealed class Writer : ContentItem
{
    [Text]
    public string? Name { get; set; }
}

[Singleton("masthead")]
public sealed class Masthead : ContentItem
{
    [Text(MaxLength = 40)]
    public string? Tagline { get; set; }
}

[Component("seo")]
public sealed class Seo
{
    [Text(MaxLength = 60)]
    public string? MetaTitle { get; set; }
}

[Block("quote")]
public sealed class QuoteBlock
{
    [Text]
    public string? Text { get; set; }
}
