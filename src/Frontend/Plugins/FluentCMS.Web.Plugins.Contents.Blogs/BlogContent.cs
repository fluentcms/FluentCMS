namespace FluentCMS.Web.Plugins.Contents.Blogs;

public class BlogContent : IContent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = String.Empty;
    public string Description { get; set; } = String.Empty;
    public string Content { get; set; } = String.Empty;
    public string Image { get; set; } = String.Empty;
}