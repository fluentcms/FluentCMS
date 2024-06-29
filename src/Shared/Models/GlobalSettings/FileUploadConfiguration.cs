namespace FluentCMS.Web.Api.Models;

public class FileUploadConfiguration
{
    public long MaxSize { get; set; } = default!;
    public int MaxCount { get; set; } = default!;
    public string AllowedExtensions { get; set; } = default!;
}
