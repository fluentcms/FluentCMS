using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Management;

// A type with rich text, for the links a write refuses and a read leaves out.
[Collection("bulletin")]
public sealed class Bulletin : ContentItem
{
    [Text(MaxLength = 100)]
    public string Title { get; set; } = "";

    [RichText]
    public RichText Body { get; set; } = RichText.Empty;
}
