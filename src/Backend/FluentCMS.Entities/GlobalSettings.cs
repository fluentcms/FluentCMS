namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public FileUploadConfig FileUpload { get; set; } = default!;
    public List<string> SuperAdmins { get; set; } = [];
    public bool Initialized { get; set; } = false;
}

public class FileUploadConfig
{
    public long MaxSize { get; set; } = default!;
    public int MaxCount { get; set; } = default!;
    public string AllowedExtensions { get; set; } = default!;
}
