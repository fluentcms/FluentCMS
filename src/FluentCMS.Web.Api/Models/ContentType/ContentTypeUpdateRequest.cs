using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class ContentTypeUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public Guid AppId { get; set; }
    
    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; } = default!;
}
