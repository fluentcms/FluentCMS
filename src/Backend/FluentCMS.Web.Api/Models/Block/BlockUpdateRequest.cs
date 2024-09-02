namespace FluentCMS.Web.Api.Models;

public class BlockUpdateRequest : BlockCreateRequest
{
    [Required]
    public Guid Id { get; set; }
}
