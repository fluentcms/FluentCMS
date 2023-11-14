namespace FluentCMS.Entities;

public class Asset : AuditEntity
{
    public Guid SiteId { get; set; }
    public Guid? FolderId { get; set; }
    public AssetType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhysicalName { get; set; }
    public long? SizeInBytes { get; set; }
}

public enum AssetType : short
{
    Folder = 0,
    File = 1,
}
