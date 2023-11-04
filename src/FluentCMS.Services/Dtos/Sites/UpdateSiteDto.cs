using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Application.Dtos.Sites;

public class UpdateSiteDto : CreateSiteDto
{
    [Required]
    public Guid Id { get; set; }
}