namespace FluentCMS.Web.Api.Models;

public class FolderUpdateRequest : FolderCreateRequest
{
    [Required]
    public Guid Id { get; set; }
}
