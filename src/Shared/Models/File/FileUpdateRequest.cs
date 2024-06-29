namespace FluentCMS.Web.Api.Models;

public class FileUpdateRequest
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public string Name { get; set; } = default!;
}
