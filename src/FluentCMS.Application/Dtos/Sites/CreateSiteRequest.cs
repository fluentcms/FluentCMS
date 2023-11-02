namespace FluentCMS.Application.Dtos.Sites;
public class CreateSiteRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] URLs { get; set; } = [];
    public Guid RoleId { get; set; }
}
