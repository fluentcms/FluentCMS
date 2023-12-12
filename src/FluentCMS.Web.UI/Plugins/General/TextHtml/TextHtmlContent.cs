using FluentCMS.Entities;

namespace FluentCMS.Web.UI.Plugins.General.TextHtml;

public class TextHtmlContent
{
    public string? Content { get; set; }
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid PluginId { get; set; }

    public TextHtmlContent()
    {
    }

    public TextHtmlContent(PluginContent pluginContent)
    {
        Id = pluginContent.Id;
        SiteId = pluginContent.SiteId;
        PluginId = pluginContent.PluginId;
        Content = pluginContent["Content"]?.ToString();
    }
}
