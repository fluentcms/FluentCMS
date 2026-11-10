using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A localized type for the preview token tests.
[Collection("brief", Localized = true)]
public sealed class Brief : ContentItem
{
    [Text(MaxLength = 100), Required, Localizable]
    public string Title { get; set; } = "";
}
