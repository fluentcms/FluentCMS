namespace FluentCMS.Web.Api.Models;

public class PagePluginDetail 
{
    public Guid Id { get; set; } = Guid.Empty;
    public string? Section { get; set; }
    public int? Cols { get; set; }
    public int? ColsMd { get; set; }
    public int? ColsLg { get; set; }
}

public class PageUpdatePluginOrdersRequest
{
    public List<PagePluginDetail> Plugins { get; set; } = [];
}
