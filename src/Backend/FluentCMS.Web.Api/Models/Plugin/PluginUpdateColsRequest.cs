namespace FluentCMS.Web.Api.Models;

public class PluginUpdateColsRequest
{
    public Guid Id { get; set; }
    public int Cols { get; set; }
    public int ColsMd { get; set; }
    public int ColsLg { get; set; }
}
