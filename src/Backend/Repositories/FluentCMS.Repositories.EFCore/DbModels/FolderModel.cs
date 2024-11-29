namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Folders")]
public class FolderModel : SiteAssociatedEntityModel
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid? ParentId { get; set; } // Foreign key
    public FolderModel? Parent { get; set; } // Navigation property
    public ICollection<FolderModel> ChildFolders { get; set; } = []; // Navigation property
    public ICollection<FileModel> ChildFiles { get; set; } = []; // Navigation property
}
