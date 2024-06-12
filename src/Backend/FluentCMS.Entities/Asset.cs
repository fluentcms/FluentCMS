namespace FluentCMS.Entities;

public class Asset : AuditableEntity
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public AssetType Type { get; set; }
    public long Size { get; set; }
    public AssetMetaData? MetaData { get; set; }
}

public class AssetMetaData
{
    public string Extension { get; set; } = default!;
    public string MimeType { get; set; } = default!;

}
public enum AssetType
{
    Folder,
    File
}
