namespace FluentCMS.Web.Api.Models;

public class FileDetailResponse
{
    public required string ContentType { get; set; }
    public required string FileName { get; set; }
    public required string FileExtension { get; set; }
    public required long Size { get; set; }
    public string Url { get; set; }
}
