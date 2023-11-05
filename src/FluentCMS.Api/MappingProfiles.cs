using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Sites;
using FluentCMS.Api.Models.Users;
using FluentCMS.Entities.Pages;
using FluentCMS.Entities.Sites;
using FluentCMS.Entities.Users;

namespace FluentCMS.Api;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // Site
        CreateMap<Site, SiteResponse>().ReverseMap();
        CreateMap<CreateSiteRequest, Site>();
        CreateMap<EditSiteRequest, Site>();

        // Page
        CreateMap<Page, PageResponse>().ReverseMap();

        // User
        CreateMap<User, UserResponse>()
            .ForMember(x => x.UserRoles, cfg => cfg.MapFrom(y => y.UserRoles.Select(z => z.RoleId.ToString())));

        // Role
        CreateMap<Role, RoleResponse>();
    }
}
