using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// A field named type, which the route's type must not be taken for.
[Collection("product")]
public sealed class Product : ContentItem
{
    [Text(MaxLength = 50), Unique]
    public string Type { get; set; } = "";
}
