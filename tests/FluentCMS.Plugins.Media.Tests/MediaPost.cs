using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Media.Tests;

// A type with every place a media reference can be: a single field, a list, rich text and a block.
[Collection("post")]
public sealed class Post : ContentItem
{
    [Text]
    public string? Title { get; set; }

    [Media]
    public MediaRef? Hero { get; set; }

    [Media]
    public MediaRefList Gallery { get; set; } = [];

    [RichText]
    public RichText Body { get; set; } = RichText.Empty;

    [Blocks(typeof(BannerBlock))]
    public BlockList Sections { get; set; } = [];
}

[Block("banner")]
public sealed class BannerBlock
{
    [Text]
    public string? Heading { get; set; }

    [Media]
    public MediaRef? Image { get; set; }
}
