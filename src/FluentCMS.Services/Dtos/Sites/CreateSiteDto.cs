using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Application.Dtos.Sites;

public class CreateSiteDto
{
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    public List<string> Urls { get; set; } = [];
}
