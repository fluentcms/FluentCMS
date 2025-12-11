using System.Text.Json.Serialization;

namespace FluentCMS.Api.Core.Models;

public class Folder : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public long Size { get; set; }

    public ICollection<File> Files { get; set; } = new List<File>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();


    [JsonIgnore]
    public Folder? ParentFolder { get; set; } = default!;
}
