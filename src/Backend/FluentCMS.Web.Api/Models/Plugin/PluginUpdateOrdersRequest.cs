namespace FluentCMS.Web.Api.Models;

public class PluginUpdateOrdersRequest
{
    public List<PluginOrder> Plugins { get; set; } = [];
}

public class PluginOrder
{
    public Guid Id { get; set; }
    public string? Section { get; set; }
}
