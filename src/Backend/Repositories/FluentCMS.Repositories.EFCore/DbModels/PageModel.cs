namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Pages")]
public class PageModel : SiteAssociatedEntityModel
{
    public string Title { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Order { get; set; }
    public string Path { get; set; } = string.Empty; // URL path, only one segment without forward slash (/)
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public bool Locked { get; set; } = false;

    public PageModel? Parent { get; set; } // Navigation property
    public LayoutModel? Layout { get; set; } // Navigation property
    public LayoutModel? EditLayout { get; set; } // Navigation property
    public LayoutModel? DetailLayout { get; set; } // Navigation property
    public ICollection<PageModel> Children { get; set; } = []; // Navigation property
}
