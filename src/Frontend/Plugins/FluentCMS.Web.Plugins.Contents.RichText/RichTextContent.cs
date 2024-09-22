namespace FluentCMS.Web.Plugins.Contents.RichText;

public class RichTextContent : IContent
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
}
