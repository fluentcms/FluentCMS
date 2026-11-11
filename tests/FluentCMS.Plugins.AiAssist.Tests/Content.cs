using FluentCMS.Schema;

namespace FluentCMS.Plugins.AiAssist.Tests;

[Collection("article", Localized = true)]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 120), Required, Localizable]
    public string Title { get; set; } = "";

    [LongText, Localizable]
    public string? Summary { get; set; }

    [RichText, Localizable]
    public RichText Body { get; set; } = RichText.Empty;

    // A text field that no site of the tests opts in.
    [Text]
    public string? Notes { get; set; }

    [Integer]
    public int? Rating { get; set; }
}
