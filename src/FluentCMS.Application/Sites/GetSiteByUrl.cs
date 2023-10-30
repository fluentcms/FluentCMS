using FluentCMS.Entities.Sites;
using MediatR;

namespace FluentCMS.Application.Sites;

public class GetSiteByUrl : IRequest<Site>
{
    public string Url { get; set; } = "";
}