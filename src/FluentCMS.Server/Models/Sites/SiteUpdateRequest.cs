using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Server.Models;

public class SiteUpdateRequest : SiteCreateRequest
{
    [Required]
    public Guid Id { get; set; }
}