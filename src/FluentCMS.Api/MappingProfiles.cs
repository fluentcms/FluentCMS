using AutoMapper;
using FluentCMS.Api.Models.Hosts;
using FluentCMS.Api.Models.Pages;
using FluentCMS.Api.Models.Sites;
using FluentCMS.Api.Models.Users;
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
        CreateMap<CreatePageRequest, Page>();
        CreateMap<EditPageRequest, Page>();

        // User
        CreateMap<User, UserResponse>()
            .ForMember(x => x.UserRoles, cfg => cfg.MapFrom(y => y.UserRoles.Select(z => z.RoleId.ToString())));
        CreateMap<CreateUserRequest, User>()
            .ForMember(x => x.UserRoles, cfg => cfg.Ignore());
        CreateMap<EditUserRequest, User>()
            .ForMember(x => x.UserRoles, cfg => cfg.Ignore());

        // Role
        CreateMap<Role, RoleResponse>();
        CreateMap<CreateRoleRequest, Role>();
        CreateMap<EditRoleRequest, Role>();
    }
}
