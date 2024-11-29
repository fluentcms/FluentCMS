namespace FluentCMS.Web.Api.Models;

public class LayoutUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public string Body { get; set; } = default!;

    [Required]
    public string Head { get; set; } = default!;
}
