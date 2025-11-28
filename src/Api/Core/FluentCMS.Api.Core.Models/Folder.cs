namespace FluentCMS.Api.Core.Models;

public class Folder : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public long Size { get; set; }

    public ICollection<File> Files { get; set; } = new List<File>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();

    public Folder ParentFolder { get; set; } = default!;
}
