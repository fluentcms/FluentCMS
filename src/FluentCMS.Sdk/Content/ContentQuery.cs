using FluentCMS.Sites;

namespace FluentCMS.Content;

/// <summary>A list query over one content type. Every API surface produces the same query, so filters behave the same everywhere.</summary>
public sealed record ContentQuery
{
    /// <summary>The filter, or <see langword="null"/> for every entry.</summary>
    public ContentFilter? Filter { get; init; }

    /// <summary>The sort keys, most significant first. The document id is always the final key.</summary>
    public IReadOnlyList<SortField> Sort { get; init; } = [];

    /// <summary>The page, or <see langword="null"/> for the first page of the default size.</summary>
    public PageRequest? Page { get; init; }

    /// <summary>The cursor of the page to read, as a previous result gave it. It cannot be combined with a page number or an offset.</summary>
    public string? Cursor { get; init; }

    /// <summary>The dotted paths of the fields to return, or empty for every field. <c>id</c> is always returned.</summary>
    public IReadOnlyList<string> Select { get; init; } = [];

    /// <summary>The dotted paths of the relations to inline, or <c>*</c> for every relation. A relation not named here is returned as its id and type.</summary>
    public IReadOnlyList<string> Populate { get; init; } = [];

    /// <summary>How many levels of relations to inline for each entry of <see cref="Populate"/>, or <see langword="null"/> for the default of one.</summary>
    public int? PopulateDepth { get; init; }

    /// <summary>Which state of the entries to read, or <see langword="null"/> for published entries.</summary>
    public QueryStatus? Status { get; init; }

    /// <summary>The locale to read as a BCP 47 tag. A localized type needs one; a non-localized type ignores it.</summary>
    public string? Locale { get; init; }

    /// <summary>Whether a missing localized value falls back to another locale on read. Filters never fall back.</summary>
    public LocaleFallback Fallback { get; init; }

    /// <summary>The site to read, or <see langword="null"/> for every site.</summary>
    public SiteId? Site { get; init; }

    /// <summary>The format to render rich text in: <c>json</c>, or a name from <see cref="Schema.RichTextFormats"/>. <see langword="null"/> means JSON.</summary>
    public string? RichTextFormat { get; init; }
}
