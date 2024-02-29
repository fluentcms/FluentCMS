using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class SetupRequest
{
    [Required]
    public string Username { get; set; } = default!;
    
    [Required]
    public string Email { get; set; } = default!;
    
    [Required]
    public string Password { get; set; } = default!;
    public string AppTemplateName { get; set; } = default!;
    public string SiteTemplateName { get; set; } = default!;
    public string AdminDomain { get; set; } = default!;
}
