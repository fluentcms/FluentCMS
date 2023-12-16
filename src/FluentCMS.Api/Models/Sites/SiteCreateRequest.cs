namespace FluentCMS.Api.Models;

public class SiteCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Urls { get; set; } = [];
    public Guid RoleId { get; set; }
}
