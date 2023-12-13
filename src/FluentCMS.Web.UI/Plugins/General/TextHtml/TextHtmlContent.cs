using FluentCMS.Entities;

namespace FluentCMS.Web.UI.Plugins.General.TextHtml;

public class TextHtmlContent
{
    public string Content { get; set; } = string.Empty;
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
        Content = pluginContent.ContainsKey("Content") ? pluginContent["Content"]?.ToString() ?? string.Empty : string.Empty;
    }

    public PluginContent ToPluginContent()
    {
        var pluginContent = new PluginContent
        {
            Id = Id,
            SiteId = SiteId,
            PluginId = PluginId,
            Type = "TextHtml"
        };
        pluginContent.Add("Content", Content);
        return pluginContent;
    }
}
