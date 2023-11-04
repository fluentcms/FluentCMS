using FluentCMS.Application.Dtos.Pages;

namespace FluentCMS.Application.Dtos.Sites;

public class SiteDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required List<string> Urls { get; set; } = [];
    public List<PageDto> Pages { get; set; } = [];
}
