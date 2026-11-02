namespace FluentCMS.Content;

/// <summary>What <see cref="IContentService.GetAsync"/> reads.</summary>
public sealed record GetOptions
{
    /// <summary>Which state of the entry to read.</summary>
    public QueryStatus Status { get; init; } = QueryStatus.Published;

    /// <summary>The locale to read as a BCP 47 tag, <c>all</c> for every locale, or <see langword="null"/> for the locale of the operation.</summary>
    public string? Locale { get; init; }

    /// <summary>Whether a missing localized value falls back to another locale.</summary>
    public LocaleFallback Fallback { get; init; }

    /// <summary>The dotted paths of the fields to return, or empty for every field. <c>id</c> is always returned.</summary>
    public IReadOnlyList<string> Select { get; init; } = [];

    /// <summary>The dotted paths of the relations to inline, or <c>*</c> for every relation.</summary>
    public IReadOnlyList<string> Populate { get; init; } = [];

    /// <summary>How many levels of relations to inline for each entry of <see cref="Populate"/>, or <see langword="null"/> for the default of one.</summary>
    public int? PopulateDepth { get; init; }

    /// <summary>The format to render rich text in: <c>json</c>, or a name from <see cref="Schema.RichTextFormats"/>. <see langword="null"/> means JSON.</summary>
    public string? RichTextFormat { get; init; }
}
