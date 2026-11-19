using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Management;

// A localized type with rich text and a relation to itself, for the URLs a write refuses and a read leaves out.
[Collection("bulletin", Localized = true)]
public sealed class Bulletin : ContentItem
{
    [Text(MaxLength = 100), Localizable]
    public string Title { get; set; } = "";

    [RichText, Localizable]
    public RichText Body { get; set; } = RichText.Empty;

    [Relation]
    public Ref<Bulletin> Related { get; set; }
}
