using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Server;

public class IdRequest
{
    [Required]
    public virtual Guid Id { get; set; }
}
