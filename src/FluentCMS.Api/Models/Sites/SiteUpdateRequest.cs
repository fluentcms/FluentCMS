namespace FluentCMS.Api.Models;

public class SiteUpdateRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public List<string> Urls { get; set; } = [];
}
