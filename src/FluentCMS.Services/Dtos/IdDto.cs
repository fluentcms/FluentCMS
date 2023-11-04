using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Application.Dtos;

public class IdDto
{
    [Required]
    public Guid Id { get; set; }
}