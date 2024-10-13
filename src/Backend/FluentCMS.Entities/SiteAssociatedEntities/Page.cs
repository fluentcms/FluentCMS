namespace FluentCMS.Entities;

public class Page : SiteAssociatedEntity
{
    public string Title { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Order { get; set; }
    public string Path { get; set; } = string.Empty; // URL path, only one segment without forward slash (/)
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public bool Locked { get; set; } = false;
    public Dictionary<string, string> Settings { get; set; } = [];
    public IEnumerable<Guid> ViewRoleIds { get; set; } = [];
    public IEnumerable<Guid> ContributorRoleIds { get; set; } = [];
    public IEnumerable<Guid> AdminRoleIds { get; set; } = [];
}
