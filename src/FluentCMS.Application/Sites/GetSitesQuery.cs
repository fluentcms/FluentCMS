using FluentCMS.Entities.Sites;
using MediatR;

namespace FluentCMS.Application.Sites;

public class GetSitesQuery : IRequest<IEnumerable<Site>>
{
    //todo: add paging, sorting, filtering, etc.
}
