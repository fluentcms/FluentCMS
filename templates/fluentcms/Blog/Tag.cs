using FluentCMS.Schema;

namespace FluentCmsApp.Blog;

[Collection("tag", Scope = ContentScope.Global)]
public sealed class Tag : ContentItem
{
    [Text(MaxLength = 50), Required]
    public string Name { get; set; } = "";

    [Slug(Source = nameof(Name)), Unique]
    public string Slug { get; set; } = "";
}
