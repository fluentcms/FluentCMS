namespace FluentCMS.Web.Api.Models;

public class BlockDetailResponse : BaseSiteAssociatedResponse
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string? Description { get; set; }
    public string Content { get; set; } = default!;
}
