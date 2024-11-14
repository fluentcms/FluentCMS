namespace FluentCMS.Web.Plugins.Block;

public class BlockContent : IContent
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
}