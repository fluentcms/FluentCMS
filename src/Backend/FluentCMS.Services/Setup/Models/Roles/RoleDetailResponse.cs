namespace FluentCMS.Services.Setup.Models;

public class RoleDetailModel : BaseSiteAssociatedModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleTypes Type { get; set; }
}
