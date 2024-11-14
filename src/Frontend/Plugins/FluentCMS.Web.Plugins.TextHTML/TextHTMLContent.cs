namespace FluentCMS.Web.Plugins.TextHTML;

public class TextHTMLContent : IContent
{
    public Guid Id { get; set; }
    public string Content { get; set; } = String.Empty;
}