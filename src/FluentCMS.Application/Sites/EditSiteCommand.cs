using MediatR;

namespace FluentCMS.Application.Sites;

public class EditSiteCommand : IRequest<Guid>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid RoleId { get; set; }
    public string[] URLs { get; set; } = Array.Empty<string>();
}
