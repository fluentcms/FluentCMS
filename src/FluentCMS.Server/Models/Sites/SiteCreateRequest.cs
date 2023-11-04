using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Server.Models;

public class SiteCreateRequest
{
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    public List<string> Urls { get; set; } = [];
}
