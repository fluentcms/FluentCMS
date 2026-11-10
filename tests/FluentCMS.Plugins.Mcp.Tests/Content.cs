using FluentCMS.Schema;

namespace FluentCMS.Plugins.Mcp.Tests;

[Collection("article")]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 20), Required]
    public string Title { get; set; } = "";
}

[Collection("page")]
public sealed class Page : ContentItem
{
    [Text, Required]
    public string Title { get; set; } = "";

    [Text]
    public string? Notes { get; set; }
}
