using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.Samples;

[Collection("author")]
public sealed class Author : ContentItem
{
    [Text(MaxLength = 100), Required, Searchable]
    public string Name { get; set; } = "";

    [RichText]
    public RichText Bio { get; set; } = RichText.Empty;

    [Media]
    public MediaRef? Avatar { get; set; }

    // The reverse side of Article.Author. It has no field attribute, so it is not a field of the schema.
    public RefList<Article> Articles { get; set; } = [];
}
