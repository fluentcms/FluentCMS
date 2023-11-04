using FluentCMS.Entities;

namespace FluentCMS.Server.Models;

public class PageResponse
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public List<Page> Children { get; set; } = [];
    public int Order { get; set; }
    public required string Path { get; set; }
}
