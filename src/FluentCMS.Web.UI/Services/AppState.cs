using FluentCMS.Api.Models;
using FluentCMS.Entities;

namespace FluentCMS.Web.UI;

public class AppState
{
    public SiteResponse Site { get; set; } = default!;
    public PageResponse Page { get; set; } = default!;
    public Layout Layout { get; set; } = default!;
    public Uri Uri { get; set; } = default!;
    public string Host { get; set; } = default!;
    public string? ViewMode { get; set; }
    public Guid? PluginId { get; set; }
}
