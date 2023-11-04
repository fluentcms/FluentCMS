namespace FluentCMS.Server.Models;

public class SiteResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required List<string> Urls { get; set; } = [];
    public List<PageResponse> Pages { get; set; } = [];
}
