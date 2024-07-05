namespace FluentCMS.Web.Api.Setup.Models;

internal class PluginTemplate
{
    public string Definition { get; set; } = default!;
    public string Section { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
