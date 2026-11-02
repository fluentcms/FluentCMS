using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.Samples;

[Collection("tag")]
public sealed class Tag : ContentItem
{
    [Text(MaxLength = 50), Required]
    public string Name { get; set; } = "";

    [Slug(Source = nameof(Name)), Unique]
    public string Slug { get; set; } = "";
}
