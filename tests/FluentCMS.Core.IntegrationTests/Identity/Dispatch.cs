using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What a preview token reads the targets of: relations to reporters and beats, and links to reporters in its body.
[Collection("dispatch")]
public sealed class Dispatch : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";

    [Relation]
    public Ref<Reporter> Reporter { get; set; }

    [Relation(Ordered = true)]
    public RefList<Reporter> Contributors { get; set; } = [];

    [Relation(Ordered = true)]
    public RefList<Beat> Beats { get; set; } = [];

    [RichText]
    public RichText Body { get; set; } = RichText.Empty;
}
