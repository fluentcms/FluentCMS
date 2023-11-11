using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Pages;
using FluentCMS.Entities;

namespace FluentCMS.Api;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // Host
        CreateMap<Host, HostResponse>();
        CreateMap<UpdateHostRequest, Host>();

        // Site
        CreateMap<Site, SiteResponse>();
        CreateMap<CreateSiteRequest, Site>();
        CreateMap<UpdateSiteRequest, Site>();

        // Page
        CreateMap<Page, PageResponse>();

        // User
        //CreateMap<User, UserResponse>()
        //    .ForMember(x => x.UserRoles, cfg => cfg.MapFrom(y => y.UserRoles.Select(z => z.RoleId.ToString())));
        //CreateMap<CreateUserRequest, User>()
        //    .ForMember(x => x.UserRoles, cfg => cfg.Ignore());
        //CreateMap<EditUserRequest, User>()
        //    .ForMember(x => x.UserRoles, cfg => cfg.Ignore());

        // Role
        CreateMap<Role, RoleDto>();
        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
    }
}
