namespace FluentCMS.Web.Api.Models;

public class PluginUpdateRequest
{
    public Guid Id { get; set; }
    public int Order { get; set; } = 0;
    public string Section { get; set; } = default!;
}
