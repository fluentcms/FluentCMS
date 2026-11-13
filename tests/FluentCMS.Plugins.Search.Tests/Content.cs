using FluentCMS.Schema;

namespace FluentCMS.Plugins.Search.Tests;

[Collection("article", Localized = true)]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 200), Required, Localizable, Searchable]
    public string Title { get; set; } = "";

    [RichText, Localizable, Searchable]
    public RichText Body { get; set; } = RichText.Empty;

    // Shared by the locales.
    [Text, Searchable]
    public string? Tagline { get; set; }

    [Text]
    public string? Notes { get; set; }

    [Text, Searchable, Hidden]
    public string? Secret { get; set; }

    [Integer]
    public int? Views { get; set; }
}

// A type with no searchable field.
[Collection("note")]
public sealed class Note : ContentItem
{
    [Text]
    public string Title { get; set; } = "";
}

[Collection("tag", Scope = ContentScope.Global)]
public sealed class Tag : ContentItem
{
    [Text, Searchable]
    public string Name { get; set; } = "";
}
