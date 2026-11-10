using FluentCMS.Schema;

namespace FluentCMS.Plugins.Mcp.Tests;

[Collection("article")]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 20), Required]
    public string Title { get; set; } = "";
}
