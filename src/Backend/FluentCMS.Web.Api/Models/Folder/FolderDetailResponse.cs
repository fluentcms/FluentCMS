namespace FluentCMS.Web.Api.Models;

public class FolderDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public long Size { get; set; }
    public List<FileDetailResponse> Files { get; set; } = [];
    public List<FolderDetailResponse> Folders { get; set; } = [];
}
