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
        CreateMap<IEnumerable<IGrouping<Guid?, Page>>, IEnumerable<PageResponse>>().ConstructUsing((x, ctx) =>
        {
            return MapItemsWithParent(x, ctx, null);
            static IEnumerable<PageResponse> MapItemsWithParent(IEnumerable<IGrouping<Guid?, Page>> x, ResolutionContext ctx, Guid? parentId)
            {
                var items = ctx.Mapper.Map<IEnumerable<PageResponse>>(x.Where(x => x.Key == parentId));
                foreach (var item in items)
                {
                    item.Children = MapItemsWithParent(x, ctx, item.Id);
                }
                return items;
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
    }
}
