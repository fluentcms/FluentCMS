using AutoMapper;
using FluentCMS.Application.Dtos.Sites;
using FluentCMS.Entities.Sites;

namespace FluentCMS.Application.Mappings;
internal class SiteMappings : Profile
{
    public SiteMappings()
    {
        CreateMap<Site, SiteDto>();
    }
}
