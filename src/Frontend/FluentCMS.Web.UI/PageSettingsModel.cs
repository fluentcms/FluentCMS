using System.ComponentModel.DataAnnotations;
namespace FluentCMS.Web.UI;

public class PageSettingsModel
{
    public Guid? Id { get; set; } = default!;

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Path { get; set; } = string.Empty;

    public Guid? ParentId { get; set; } = default;
    public Guid? LayoutId { get; set; } = default;
    public Guid? EditLayoutId { get; set; } = default;
    public Guid? DetailLayoutId { get; set; } = default;

    [Required]
    public int Order { get; set; } = default;

    [Required]
    public ICollection<Guid> ViewRoleIds { get; set; } = [];

    [Required]
    public ICollection<Guid> ContributorRoleIds { get; set; } = [];

    [Required]
    public ICollection<Guid> AdminRoleIds { get; set; } = [];

    #region Head 
    
    public string MetaTitle { get; set; } = default!;
    public string MetaDescription { get; set; } = default!;
    public string Robots { get; set; } = default!;
    public string OgType { get; set; } = default!;
    public string Head { get; set; } = default!;

    #endregion

    public PageCreateRequest ToCreateRequest(Guid siteId)
    {
        return new PageCreateRequest()
        {
            SiteId = siteId,
            Title = Title,       
            Path = Path,
            ParentId = ParentId,
            LayoutId = LayoutId,
            EditLayoutId = EditLayoutId,
            DetailLayoutId = DetailLayoutId,
            Order = Order,
            AdminRoleIds = AdminRoleIds,
            ContributorRoleIds = ContributorRoleIds,
            ViewRoleIds = ViewRoleIds,
        };
    }

    public PageUpdateRequest ToUpdateRequest(Guid siteId, Guid pageId)
    {
        return new PageUpdateRequest()
        {
            Id = pageId,
            SiteId = siteId,
            Title = Title,       
            Path = Path,
            ParentId = ParentId,
            LayoutId = LayoutId,
            EditLayoutId = EditLayoutId,
            DetailLayoutId = DetailLayoutId,
            Order = Order,
            AdminRoleIds = AdminRoleIds,
            ContributorRoleIds = ContributorRoleIds,
            ViewRoleIds = ViewRoleIds,
        };
    }

    public SettingsUpdateRequest ToSettingsRequest(Guid id)
    {
        return new SettingsUpdateRequest
        {
            Id = id,
            Settings = new Dictionary<string, string>
            {
                ["MetaTitle"] = MetaTitle,
                ["MetaDescription"] = MetaDescription,
                ["Robots"] = Robots,
                ["OgType"] = OgType,
                ["Head"] = Head
            }
        };
    }
}
