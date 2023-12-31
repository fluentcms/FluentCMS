namespace FluentCMS.Web.Api.Models;

public class SetupRequest
{
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string AppTemplateName { get; set; } = default!;
    public string SiteTemplateName { get; set; } = default!;
    public string AdminDomain { get; set; } = default!;
}
