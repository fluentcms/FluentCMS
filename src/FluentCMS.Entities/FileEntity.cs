namespace FluentCMS.Entities;

public class FileEntity : AuditableEntity
{
    public required string ContentType { get; set; }
    public required string FileName { get; set; }
    public required string FileExtension { get; set; }
    public required long Size { get; set; }
}
