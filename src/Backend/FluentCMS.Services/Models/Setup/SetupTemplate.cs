namespace FluentCMS.Services.Models.Setup;

public class SetupTemplate
{
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string Url { get; set; } = default!;
    public SiteTemplate Site { get; set; } = default!;
    public List<PluginDefinition> PluginDefinitions { get; set; } = [];
    public List<ContentTypeTemplate> ContentTypes { get; set; } = [];
}

