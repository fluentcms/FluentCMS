namespace FluentCMS.Web.Api.Models.Folder;

public class FolderMoveRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public Guid ParentId { get; set; }
}
