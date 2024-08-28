namespace FluentCMS.Web.Plugins.Contents.Block;

public class BlockContent : IContent
{
    public Guid Id { get; set; }
    public string Template { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
}