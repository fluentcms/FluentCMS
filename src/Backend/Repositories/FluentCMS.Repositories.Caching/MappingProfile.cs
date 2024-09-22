using AutoMapper;

namespace FluentCMS.Repositories.Caching;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<ApiToken, ApiToken>();
        CreateMap<AuditableEntity, AuditableEntity>();
        CreateMap<Block, Block>();
        CreateMap<Entity, Entity>();
        CreateMap<GlobalSettings, GlobalSettings>();
        CreateMap<PluginDefinition, PluginDefinition>();
        CreateMap<Layout, Layout>();
        CreateMap<Page, Page>();
        CreateMap<Permission, Permission>();
        CreateMap<Plugin, Plugin>();
        CreateMap<PluginContent, PluginContent>();
        CreateMap<Role, Role>();
        CreateMap<Site, Site>();
        CreateMap<SiteAssociatedEntity, SiteAssociatedEntity>();
        CreateMap<UserRole, UserRole>();
    }
}
