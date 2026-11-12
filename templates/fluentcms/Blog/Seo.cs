using FluentCMS.Schema;

namespace FluentCms_App.Blog;

[Component]
public sealed class Seo
{
    [Text(MaxLength = 70)]
    public string? MetaTitle { get; set; }

    [Text(MaxLength = 160)]
    public string? MetaDescription { get; set; }
}
