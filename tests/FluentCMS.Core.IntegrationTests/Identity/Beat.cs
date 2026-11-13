using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A target every site shares, for the preview token tests.
[Collection("beat", Scope = ContentScope.Global)]
public sealed class Beat : ContentItem
{
    [Text(MaxLength = 50), Required]
    public string Name { get; set; } = "";
}
