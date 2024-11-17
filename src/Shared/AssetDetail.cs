namespace FluentCMS;

public class AssetDetail
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContentType { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string? Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsParentFolder { get; set; } = false;
}
