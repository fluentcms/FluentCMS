namespace FluentCMS.Web.Api.Models;

public class AssetDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public long Size { get; set; }
    public AssetType Type { get; set; }
    public AssetMetaData? MetaData { get; set; }
}
