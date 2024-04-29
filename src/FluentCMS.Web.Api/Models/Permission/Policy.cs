namespace FluentCMS.Web.Api.Models;

public class Policy
{
    [Required]
    public string Area { get; set; } = default!;

    [Required]
    public string Action { get; set; } = default!;
}
