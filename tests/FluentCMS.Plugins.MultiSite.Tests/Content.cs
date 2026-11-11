using FluentCMS.Schema;

namespace FluentCMS.Plugins.MultiSite.Tests;

// Each site has its own articles.
[Collection("article")]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}

// One set of tags for every site.
[Collection("tag", Scope = ContentScope.Global)]
public sealed class Tag : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Name { get; set; } = "";
}
