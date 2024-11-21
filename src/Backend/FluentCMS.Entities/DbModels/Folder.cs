namespace FluentCMS.Repositories.EFCore.DbModels;

public class Folder : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid? ParentId { get; set; } // Foreign key
    public Folder? Parent { get; set; } // Navigation property
    public ICollection<Folder> ChildFolders { get; set; } = []; // Navigation property
    public ICollection<File> ChildFiles { get; set; } = []; // Navigation property
}
