namespace Admin;

public class PageFormModel
{
    public Guid? Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; }
    public int Order { get; set; }

    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? OgType { get; set; }

    public string? Head { get; set; }   
}