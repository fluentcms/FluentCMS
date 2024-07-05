namespace FluentCMS.Web.Api.Setup.Models;

internal class PluginTemplate
{
    public string Definition { get; set; } = default!;
    public string Section { get; set; } = default!;
    public string Type { get; set; } = default!;
    public List<Dictionary<string, object>> Content { get; set; } = default!;
}
