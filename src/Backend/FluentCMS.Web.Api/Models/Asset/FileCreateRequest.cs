namespace FluentCMS.Web.Api.Models;

public class FileCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public Guid? ParentId { get; set; }
}
