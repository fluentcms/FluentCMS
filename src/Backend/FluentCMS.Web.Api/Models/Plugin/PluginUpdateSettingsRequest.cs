namespace FluentCMS.Web.Api.Models;

public class PluginUpdateSettingsRequest
{
    public Guid Id { get; set; }
    public Dictionary<string, string> Settings { get; set; } = [];
}
