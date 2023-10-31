using FluentCMS.Entities.Sites;
using MediatR;

namespace FluentCMS.Application.Sites;

public class GetSiteByIdQuery : IRequest<Site>
{
    public Guid SiteId { get; set; }
}
