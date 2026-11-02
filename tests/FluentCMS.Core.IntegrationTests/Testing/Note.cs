using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Testing;

[Collection("note")]
public sealed class Note : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";

    [Integer]
    public int Views { get; set; }
}
