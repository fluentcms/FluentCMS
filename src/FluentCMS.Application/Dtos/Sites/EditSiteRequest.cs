namespace FluentCMS.Application.Dtos.Sites;
public class EditSiteRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid RoleId { get; set; }
    public ICollection<string> URLs { get; set; } = new List<string>();
}
