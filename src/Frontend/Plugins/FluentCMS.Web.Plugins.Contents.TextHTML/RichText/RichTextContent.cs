namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public class RichTextContent : IContent
{
    public Guid Id { get; set; }
    public string Content { get; set; } = String.Empty;
}