namespace FluentCMS.Web.Api.Models;

public class FolderDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public long Size { get; set; }
    public List<AssetDetailResponse> Children { get; set; } = default!;
}
