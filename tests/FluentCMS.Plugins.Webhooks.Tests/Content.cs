using FluentCMS.Schema;

namespace FluentCMS.Plugins.Webhooks.Tests;

[Collection("article")]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}

[Collection("page")]
public sealed class Page : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}
