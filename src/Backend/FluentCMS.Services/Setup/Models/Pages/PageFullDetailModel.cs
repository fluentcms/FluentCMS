namespace FluentCMS.Services.Setup.Models;

public class PageFullDetailModel
{
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public int Order { get; set; }
    public string Path { get; set; } = default!;
    public string FullPath { get; set; } = default!;
    public LayoutDetailModel Layout { get; set; } = default!;
    public LayoutDetailModel DetailLayout { get; set; } = default!;
    public LayoutDetailModel EditLayout { get; set; } = default!;
    public SiteDetailModel Site { get; set; } = default!;
    public Dictionary<string, List<PluginDetailModel>> Sections { get; set; } = [];
    public bool Locked { get; set; } = false;
    public UserRoleDetailModel User { get; set; } = default!;
}
