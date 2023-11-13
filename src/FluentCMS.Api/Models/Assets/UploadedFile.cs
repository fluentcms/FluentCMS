namespace FluentCMS.Api.Models;

public class UploadedFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool Successful { get; set; }
}
