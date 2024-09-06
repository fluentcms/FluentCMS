namespace FluentCMS.Web.Api.Models;

public class SetupRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    [Required]
    public string Template { get; set; } = default!;

    [Required]
    [DomainName]
    public string Url { get; set; } = default!;
}
