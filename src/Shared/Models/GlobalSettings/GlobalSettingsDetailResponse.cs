namespace FluentCMS.Web.Api.Models;

public class GlobalSettingsDetailResponse : BaseAuditableResponse
{
    public SmtpServerConfiguration Email { get; set; } = default!;
    public FileUploadConfiguration FileUpload { get; set; } = default!;
}
