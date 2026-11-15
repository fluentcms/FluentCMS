using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// Read by the Public role in the tag header tests, with the topic of its note a level down.
[Collection("pin")]
public sealed class Pin : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";

    [Relation]
    public Ref<Note> Note { get; set; }
}
