namespace FluentCMS.Web.Api.Models;

public class RoleSimpleResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<Policy> Policies { get; set; } = [];
}
