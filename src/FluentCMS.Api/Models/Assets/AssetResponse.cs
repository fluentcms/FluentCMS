using FluentCMS.Entities;

namespace FluentCMS.Api.Models;

public class AssetResponse
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid? FolderId { get; set; }
    public AssetType Type { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? SizeInBytes { get; set; }
}
