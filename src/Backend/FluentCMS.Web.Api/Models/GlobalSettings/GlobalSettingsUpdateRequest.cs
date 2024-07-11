namespace FluentCMS.Web.Api.Models;

public class GlobalSettingsUpdateRequest
{
    public SmtpServerConfig Email { get; set; } = default!;
}
