namespace FluentCMS.Api.Core.Services.Models;

public class SetupTemplate
{
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string TemplateName { get; set; } = default!;
    public SiteTemplate Site { get; set; } = default!;
}

