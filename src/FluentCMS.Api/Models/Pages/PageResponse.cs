namespace FluentCMS.Api.Models;

public class PageResponse
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public IEnumerable<PageResponse> Children { get; set; } = [];
    public int Order { get; set; }
    public required string Path { get; set; }
}
