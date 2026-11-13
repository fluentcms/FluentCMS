using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A target in one site, which the preview token tests hold to a row rule on its desk, with a relation for a second level.
[Collection("reporter")]
public sealed class Reporter : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Name { get; set; } = "";

    [Text(MaxLength = 20)]
    public string Desk { get; set; } = "";

    [Relation]
    public Ref<Beat> Beat { get; set; }
}
