using FluentCMS.Schema;

namespace FluentCMS.Samples.Blog;

[Block("hero")]
public sealed class HeroBlock
{
    [Text]
    public string Heading { get; set; } = "";

    [Text]
    public string? Subheading { get; set; }
}

[Block("quote")]
public sealed class QuoteBlock
{
    [LongText, Required]
    public string Text { get; set; } = "";

    [Text]
    public string? Attribution { get; set; }
}

[Block("gallery")]
public sealed class GalleryBlock
{
    [Text]
    public string? Caption { get; set; }

    [Integer]
    public int Columns { get; set; } = 3;
}
