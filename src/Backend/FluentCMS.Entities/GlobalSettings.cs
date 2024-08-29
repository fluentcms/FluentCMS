namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public FileUploadConfig FileUpload { get; set; } = default!;
}

public class FileUploadConfig
{
    public long MaxSize { get; set; } = default!;
    public int MaxCount { get; set; } = default!;
    public string AllowedExtensions { get; set; } = default!;
}
