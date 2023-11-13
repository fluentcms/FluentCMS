namespace FluentCMS.Api.Models;

public class AssetResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public Guid SiteId { get; set; }
}
