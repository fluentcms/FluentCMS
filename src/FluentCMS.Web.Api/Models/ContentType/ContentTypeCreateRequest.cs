using FluentCMS.Web.Api.Validation;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class ContentTypeCreateRequest
{
    [Required]
    [Slug]
    public string Slug { get; set; } = default!;

    [Required]
    [Length(3,20)]
    public string Title { get; set; } = default!;
    public string? Description { get; set; } = default!;
}
