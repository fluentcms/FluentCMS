using FluentCMS.Entities.Sites;
using MediatR;

namespace FluentCMS.Application.Sites;

public class GetSiteByUrlQuery : IRequest<Site>
{
    public string Url { get; set; } = "";
}