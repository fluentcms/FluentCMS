namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public class BlockContent : IContent
{
    public Guid Id { get; set; }
    public string Template { get; set; } = String.Empty;
    public Dictionary<string, object> Settings { get; set; } = [];
}