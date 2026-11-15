using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A localized type with a slug, for the preview URL tests.
[Collection("story", Localized = true)]
public sealed class Story : ContentItem
{
    [Text(MaxLength = 100), Required, Localizable]
    public string Title { get; set; } = "";

    [Slug]
    public string? Slug { get; set; }

    [Text(MaxLength = 100)]
    public string? Code { get; set; }
}
