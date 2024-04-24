using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class ContentTypeCreateRequest
{
    [Required]
    [Slug]
    public string Slug { get; set; } = default!;

    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; } = default!;
}
