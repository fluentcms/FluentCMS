namespace FluentCMS.Web.Api.Models;

public class UserUpdateRequest
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public ICollection<Guid> RoleIds { get; set; } = new List<Guid>();
}
