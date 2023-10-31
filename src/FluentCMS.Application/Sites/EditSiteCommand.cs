using MediatR;

namespace FluentCMS.Application.Sites;

public class EditSiteCommand : IRequest<Guid>
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
