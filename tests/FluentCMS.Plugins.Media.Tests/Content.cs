using FluentCMS.Schema;

namespace FluentCMS.Plugins.Media.Tests;

[Collection("post")]
public sealed class Post : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}

[Collection("secret")]
public sealed class Secret : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}
