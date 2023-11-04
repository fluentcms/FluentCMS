using FluentCMS.Entities;

namespace FluentCMS.Application.Dtos.Pages;

public class PageDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public List<Page> Children { get; set; } = [];
    public int Order { get; set; }
    public required string Path { get; set; }
}
