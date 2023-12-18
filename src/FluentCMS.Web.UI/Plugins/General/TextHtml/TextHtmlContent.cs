namespace FluentCMS.Web.UI.Plugins.General.TextHtml;

public class TextHtmlContent
{
    public string Content { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid PluginId { get; set; }
    public string Type { get; set; } = "TextHtml";
}
