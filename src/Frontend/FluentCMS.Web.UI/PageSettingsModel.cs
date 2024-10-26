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
    public Guid? DetailLayoutId { get; set; } = default;

    [Required]
    public int Order { get; set; } = default;

    [Required]
    public ICollection<Guid> ViewRoleIds { get; set; } = [];

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
            ParentId = ParentId != Guid.Empty ? ParentId : default!,
            LayoutId = LayoutId != Guid.Empty ? LayoutId : default!,
            DetailLayoutId = DetailLayoutId != Guid.Empty ? DetailLayoutId : default!,
            Order = Order,
            AdminRoleIds = AdminRoleIds,
            ViewRoleIds = ViewRoleIds,
        };
    }

    public void Initialize(PageDetailResponse response)
    {
        Title = response.Title ?? string.Empty;
        Path = response.Path ?? string.Empty;
        ParentId = response.ParentId;
        LayoutId = response.LayoutId;
        DetailLayoutId = response.DetailLayoutId;
        Order = response.Order;
        AdminRoleIds = response.AdminRoleIds ?? [];
        ViewRoleIds = response.ViewRoleIds ?? [];

        var settings = response.Settings ?? [];

        settings.TryGetValue("MetaTitle", out var metaTitle);
        settings.TryGetValue("MetaDescription", out var metaDescription);
        settings.TryGetValue("OgType", out var ogType);
        settings.TryGetValue("Robots", out var robots);
        settings.TryGetValue("Head", out var head);

        MetaTitle = metaTitle ?? string.Empty;
        MetaDescription = metaDescription ?? string.Empty;
        OgType = ogType ?? string.Empty;
        Robots = robots ?? string.Empty;
        Head = head ?? string.Empty;
    }

    public PageUpdateRequest ToUpdateRequest(Guid siteId, Guid pageId)
    {
        return new PageUpdateRequest()
        {
            Id = pageId,
            SiteId = siteId,
            Title = Title,
            Path = Path,
            ParentId = ParentId != Guid.Empty ? ParentId : default!,
            LayoutId = LayoutId != Guid.Empty ? LayoutId : default!,
            DetailLayoutId = DetailLayoutId != Guid.Empty ? DetailLayoutId : default!,
            Order = Order,
            AdminRoleIds = AdminRoleIds,
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
