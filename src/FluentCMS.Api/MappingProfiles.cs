using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;

namespace FluentCMS.Api;

/// <summary>
/// AutoMapper profile class defining mappings between entity and model classes.
/// </summary>
public class MappingProfiles : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MappingProfiles"/> class.
    /// </summary>
    /// <remarks>
    /// Configures the mappings for various domain entities and their corresponding data transfer objects.
    /// </remarks>
    public MappingProfiles()
    {
        // Host mappings
        CreateMap<HostUpdateRequest, Host>();

        // Site mappings
        CreateMap<Site, SiteResponse>();
        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();

        // Page mappings
        CreateMap<Page, PageResponse>();

        // Mapping for lists of Page entities to PageResponse models
        CreateMap<List<Page>, List<PageResponse>>()
            .ForMember("Item", opt => opt.Ignore())
            .ConstructUsing((x, ctx) =>
            {
                return MapPagesWithParentId(x, ctx, null, "");
                static List<PageResponse> MapPagesWithParentId(List<Page> pages, ResolutionContext ctx, Guid? parentId, string pathPrefix)
                {
                    var items = ctx.Mapper.Map<List<PageResponse>>(pages.Where(page => page.ParentId == parentId));
                    foreach (var item in items)
                    {
                        item.Path = string.Join("/", pathPrefix, item.Path);
                        item.Children = MapPagesWithParentId(pages, ctx, item.Id, item.Path);
                    }
                    return items.ToList();
                }
            });

        // Role mappings
        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();

        CreateMap<UserRegisterRequest, User>();
        CreateMap<User, UserDetailResponse>();
    }
}
