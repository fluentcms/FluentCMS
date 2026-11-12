using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCms_App.Blog;

[Collection("author")]
public sealed class Author : ContentItem
{
    [Text(MaxLength = 100), Required, Searchable]
    public string Name { get; set; } = "";

    [Slug(Source = nameof(Name)), Unique]
    public string Slug { get; set; } = "";

    [RichText]
    public RichText Bio { get; set; } = RichText.Empty;

    [Media]
    public MediaRef? Avatar { get; set; }
}
