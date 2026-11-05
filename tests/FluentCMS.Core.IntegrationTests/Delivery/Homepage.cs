using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Delivery;

[Singleton("homepage")]
public sealed class Homepage : ContentItem
{
    [Text(MaxLength = 100)]
    public string Headline { get; set; } = "";
}
