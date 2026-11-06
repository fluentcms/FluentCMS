using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// Read by the Public role in the caching tests.
[Collection("note")]
public sealed class Note : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";

    [Text(MaxLength = 100), Unique]
    public string Slug { get; set; } = "";

    [Relation]
    public Ref<Topic> Topic { get; set; }
}
