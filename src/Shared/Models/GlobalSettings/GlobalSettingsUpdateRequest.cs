namespace FluentCMS.Web.Api.Models;

public class GlobalSettingsUpdateRequest
{
    public SmtpServerConfiguration Email { get; set; } = default!;
    public FileUploadConfiguration FileUpload { get; set; } = default!;
}
