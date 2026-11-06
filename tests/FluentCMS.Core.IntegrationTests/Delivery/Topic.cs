using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// What a note in the caching tests populates.
[Collection("topic")]
public sealed class Topic : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Name { get; set; } = "";
}
