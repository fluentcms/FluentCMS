using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// Read by keys only in the caching tests.
[Collection("memo")]
public sealed class Memo : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}
