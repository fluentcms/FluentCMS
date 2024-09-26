using FluentCMS.Services.Models;

namespace FluentCMS.Web.Api.Models;

public class PluginUpdateOrdersRequest
{
    [Required]
    public List<PluginOrder> PluginOrders { get; set; } = [];
}
