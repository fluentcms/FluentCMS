namespace FluentCMS.Web.Api.Models;

public class PageUpdatePluginOrdersRequest
{
    public List<PagePluginDetail> Plugins { get; set; } = [];
}

public class PagePluginDetail
{
    public Guid Id { get; set; }
    public int? Order { get; set; }
    public string? Section { get; set; }
    public int? Cols { get; set; }
    public int? ColsMd { get; set; }
    public int? ColsLg { get; set; }
}
