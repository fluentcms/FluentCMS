using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;

namespace FluentCMS.Api;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // Host
        CreateMap<Host, HostResponse>();
        CreateMap<HostUpdateRequest, Host>();

        // Site
        CreateMap<Site, SiteResponse>();
        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();

        // Page
        CreateMap<Page, PageResponse>();

        CreateMap<List<Page>, List<PageResponse>>()
            .ForMember("Item", opt => opt.Ignore())
            .ConstructUsing((x, ctx) =>
        {
            return MapPagesWithParentId(x, ctx, null, "");
            static List<PageResponse> MapPagesWithParentId(List<Page> x, ResolutionContext ctx, Guid? parentId, string pathPrefix)
            {
                var items = ctx.Mapper.Map<List<PageResponse>>(x.Where(x => x.ParentId == parentId));
                foreach (var item in items)
                {
                    item.Path = string.Join("/", pathPrefix, item.Path);
                    item.Children = MapPagesWithParentId(x, ctx, item.Id, item.Path);
                }
                return items.ToList();
            }
        });
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

        CreateMap<RoleUpdateRequest, Role>();
    }
}
