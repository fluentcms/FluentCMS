using System.ComponentModel.DataAnnotations;
using FluentCMS.Web.Api.Validation;

namespace FluentCMS.Web.Api.Models;

public class AppCreateRequest
{
    [Required]
    [Slug]    
    public string Slug { get; set; } = default!;
    
    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
}
