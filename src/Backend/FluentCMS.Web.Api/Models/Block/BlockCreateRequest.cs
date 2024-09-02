namespace FluentCMS.Web.Api.Models;

public class BlockCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public string Category { get; set; } = default!;
    
    public string? Description { get; set; } = default!;
    
    [Required]
    public string Content { get; set; } = default!;
}
