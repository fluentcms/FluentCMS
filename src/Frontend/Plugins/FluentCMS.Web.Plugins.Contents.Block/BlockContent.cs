namespace FluentCMS.Web.Plugins.Contents.Block;

public class BlockContent : IContent
{
    public Guid Id { get; set; }
    public string Template { get; set; } = String.Empty;
    public Dictionary<string, object> Settings { get; set; } = [];
}