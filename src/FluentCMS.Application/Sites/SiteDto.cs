namespace FluentCMS.Application.Sites;

public class SiteDto
{
    public Guid Id { get; set; }
    public string? CreatedBy { get; set; } 
    public DateTime CreatedAt { get; set; } 
    public string? LastUpdatedBy { get; set; } 
    public DateTime LastUpdatedAt { get; set; } 
    public string? Name { get; set; } 
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid RoleId { get; set; } 
}