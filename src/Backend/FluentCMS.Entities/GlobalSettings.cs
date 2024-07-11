namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public SmtpServerConfig Email { get; set; } = default!;
    public FileUploadConfig FileUpload { get; set; } = default!;
}

public class SmtpServerConfig
{
    public string Server { get; set; } = default!;
    public int Port { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public bool EnableSsl { get; set; }
    public string From { get; set; } = default!;
}

public class FileUploadConfig
{
    public long MaxSize { get; set; } = default!;
    public int MaxCount { get; set; } = default!;
    public string AllowedExtensions { get; set; } = default!;
}
