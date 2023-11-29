using FluentCMS.Api.Models;
using FluentCMS.Entities;

namespace FluentCMS.Web.UI;

public class AppState
{
    public SiteResponse? Site { get; set; }
    public PageResponse? Page { get; set; }
    public Layout? Layout { get; set; }
    public string? Route { get; set; }
}
