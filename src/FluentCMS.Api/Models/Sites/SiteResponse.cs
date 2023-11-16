namespace FluentCMS.Api.Models;

public class SiteResponse
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = default;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Urls { get; set; } = [];
    public Guid RoleId { get; set; }
    public IEnumerable<PageResponse> Pages { get; set; } = [];
}
