using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.Samples;

[Singleton("site-settings")]
public sealed class SiteSettings : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string SiteName { get; set; } = "";

    [LongText]
    public string? Description { get; set; }

    [Media]
    public MediaRef? Logo { get; set; }
}
