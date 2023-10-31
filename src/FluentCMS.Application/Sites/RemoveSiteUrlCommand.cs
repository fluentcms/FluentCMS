using MediatR;

namespace FluentCMS.Application.Sites;

public class RemoveSiteUrlCommand : IRequest
{
    public Guid SiteId { get; set; }
    public string Url { get; set; } = "";
}
