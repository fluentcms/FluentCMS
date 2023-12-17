using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;

namespace FluentCMS.Api;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // Host
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

        // Role mapping
        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();

        CreateMap<RoleUpdateRequest, Role>();
    }
}
