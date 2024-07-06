using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public class TextHTMLContent: IContent
{
    public Guid Id { get; set; }
    public string Content { get; set; } = String.Empty;
}