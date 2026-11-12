using System.Text.Json.Nodes;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Generators.Tests.Kitchen;

// A field of every type the generator knows, at the top level, in components and in blocks. The generator runs on
// this project, so the tests use the builders it emitted for these types against a running content service.
[Collection("kitchen")]
public sealed class Kitchen : ContentItem
{
    [Text]
    public string? Name { get; set; }

    [LongText]
    public string? Notes { get; set; }

    [RichText]
    public RichText Story { get; set; } = RichText.Empty;

    [Integer]
    public int Seats { get; set; }

    [Decimal]
    public decimal Rent { get; set; }

    [Boolean]
    public bool Open { get; set; }

    [Date]
    public DateOnly? OpenedOn { get; set; }

    [DateTime]
    public DateTimeOffset? InspectedAt { get; set; }

    [Email]
    public string? Contact { get; set; }

    [Url]
    public string? Website { get; set; }

    [Slug(Source = nameof(Name))]
    public string? Slug { get; set; }

    [Select]
    public Course Course { get; set; }

    [Select]
    public List<Course> Courses { get; set; } = [];

    [Json]
    public JsonNode? Metadata { get; set; }

    [Relation]
    public Ref<Kitchen>? Parent { get; set; }

    [Relation]
    public RefList<Kitchen> Neighbours { get; set; } = [];

    [Media]
    public MediaRef? Photo { get; set; }

    [Media]
    public MediaRefList Photos { get; set; } = [];

    [Media]
    public List<MediaRef> Album { get; set; } = [];

    [Component]
    public Shelf Shelf { get; set; } = new();

    [Component]
    public List<Shelf> Shelves { get; set; } = [];

    [Blocks(typeof(Recipe), typeof(Note))]
    public BlockList Menu { get; set; } = [];
}

public enum Course
{
    Starter,
    Main,
    Dessert,
}

[Component]
public sealed class Shelf
{
    [Text]
    public string? Label { get; set; }

    [Integer]
    public int Height { get; set; }

    [Media]
    public MediaRef? Picture { get; set; }

    [Component]
    public Bin Bin { get; set; } = new();
}

[Component("bin")]
public sealed class Bin
{
    [Boolean]
    public bool Full { get; set; }
}

[Block("recipe")]
public sealed class Recipe
{
    [Text]
    public string? Title { get; set; }

    [LongText]
    public string? Steps { get; set; }

    [Select]
    public Course Course { get; set; }

    [Select]
    public List<Course> Pairings { get; set; } = [];

    [Integer]
    public int Serves { get; set; }

    [Decimal]
    public decimal Cost { get; set; }

    [Boolean]
    public bool Vegan { get; set; }

    [Date]
    public DateOnly? Created { get; set; }

    [DateTime]
    public DateTimeOffset? Cooked { get; set; }

    [Email]
    public string? Chef { get; set; }

    [Url]
    public string? Source { get; set; }

    [RichText]
    public RichText Story { get; set; } = RichText.Empty;

    [Json]
    public JsonNode? Extra { get; set; }

    [Media]
    public MediaRef? Image { get; set; }
}

[Block("note")]
public sealed class Note
{
    [Text]
    public string? Text { get; set; }
}
