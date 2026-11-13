using FluentCMS.Schema;

namespace FluentCms_App.Content;

// A content type is a class: its table, its Management and Delivery endpoints (/api/delivery/v1/article) and its typed
// filter and sort builder (ArticleFields) come from it. Add your own types next to it.
[Collection("article")]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 200), Required, Searchable]
    public string Title { get; set; } = "";

    [Slug(Source = nameof(Title)), Unique]
    public string Slug { get; set; } = "";

    [RichText]
    public RichText Body { get; set; } = RichText.Empty;

    [DateTime, Indexed]
    public DateTimeOffset? PublishedOn { get; set; }
}
