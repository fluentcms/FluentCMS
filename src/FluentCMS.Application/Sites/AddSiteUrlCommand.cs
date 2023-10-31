using MediatR;

namespace FluentCMS.Application.Sites;

public class AddSiteUrlCommand : IRequest
{
    public Guid SideId { get; set; }
    public string NewUrl { get; set; } = "";
}
