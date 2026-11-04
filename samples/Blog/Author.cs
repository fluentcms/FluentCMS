using FluentCMS.Schema;

namespace FluentCMS.Samples.Blog;

[Collection("author")]
public sealed class Author : ContentItem
{
    [Text(MaxLength = 100), Required, Searchable]
    public string Name { get; set; } = "";

    [RichText]
    public RichText Bio { get; set; } = RichText.Empty;

    // The reverse side of Article.Author. It has no field attribute, so it is not a field of the schema.
    public RefList<Article> Articles { get; set; } = [];
}
