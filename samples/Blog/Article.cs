using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Samples.Blog;

[Collection("article", Localized = true)]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 200), Required, Localizable, Searchable]
    public string Title { get; set; } = "";

    [Slug(Source = nameof(Title)), Localizable, Unique]
    public string Slug { get; set; } = "";

    [RichText, Localizable]
    public RichText Body { get; set; } = RichText.Empty;

    // The reverse side shows in introspection only; an author's articles are a list of article filtered by author.
    [Relation(Reverse = "articles")]
    public Ref<Author> Author { get; set; }

    [Relation(Ordered = true)]
    public RefList<Tag> Tags { get; set; } = [];

    [Media]
    public MediaRef? Hero { get; set; }

    [Blocks(typeof(HeroBlock), typeof(QuoteBlock), typeof(GalleryBlock))]
    public BlockList Sections { get; set; } = [];

    [Component]
    public Seo Seo { get; set; } = new();

    [Select, Indexed]
    public ArticleCategory Category { get; set; }

    [DateTime, Indexed]
    public DateTimeOffset? PublishedOn { get; set; }

    [VisibleWhen(nameof(IsVideo)), RequiredWhen(nameof(IsVideo))]
    [Url]
    public string? VideoUrl { get; set; }

    public static Condition IsVideo => Condition.Field(nameof(Category)).Equals(ArticleCategory.Video);
}
