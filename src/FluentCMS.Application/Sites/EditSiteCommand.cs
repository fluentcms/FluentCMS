using MediatR;

namespace FluentCMS.Application.Sites;

public class EditSiteCommand : IRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
