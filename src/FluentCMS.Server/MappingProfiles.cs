using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Server.Models;

namespace FluentCMS.Server;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // Site
        CreateMap<Site, SiteResponse>().ReverseMap();
        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();

        // Page
        CreateMap<Page, PageResponse>().ReverseMap();
    }
}