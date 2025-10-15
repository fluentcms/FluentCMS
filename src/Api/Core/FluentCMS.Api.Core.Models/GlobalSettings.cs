namespace FluentCMS.Api.Core.Models;

public class GlobalSettings : AuditableEntity
{
    public List<string> SuperAdmins { get; set; } = [];
    public FileUploadConfig FileUpload { get; set; } = default!;
}

public class FileUploadConfig
{
    public long MaxSize { get; set; } = default!;
    public int MaxCount { get; set; } = default!;
    public string AllowedExtensions { get; set; } = default!;
}
