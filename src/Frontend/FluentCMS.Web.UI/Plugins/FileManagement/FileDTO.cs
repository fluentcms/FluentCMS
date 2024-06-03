// TODO

namespace FluentCMS.Web.UI.Plugins.FileManagement;

public class FileDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long Size { get; set; } = 0;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
