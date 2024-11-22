namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Sites")]
public class SiteModel : AuditableEntityModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Urls { get; set; } = default!;
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }

    public LayoutModel Layout { get; set; } = default!; // Navigation property
    public LayoutModel DetailLayout { get; set; } = default!; // Navigation property
    public LayoutModel EditLayout { get; set; } = default!; // Navigation property
    public ICollection<BlockModel> Blocks { get; set; } = []; // Navigation property
    public ICollection<ContentModel> Contents { get; set; } = []; // Navigation property
    public ICollection<PageModel> Pages { get; set; } = []; // Navigation property
    public ICollection<FileModel> Files { get; set; } = []; // Navigation property
    public ICollection<FolderModel> Folders { get; set; } = []; // Navigation property
}
