using FluentCMS.Web.Api.TypeConverters;

namespace FluentCMS.Web.Api;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region Site

        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();
        CreateMap<Site, SiteDetailResponse>();
        CreateMap<Site, SiteFullDetailResponse>();

        #endregion

        #region PluginDefinition

        CreateMap<PluginDefinitionCreateRequest, PluginDefinition>();
        CreateMap<PluginDefinition, PluginDefinitionDetailResponse>();

        #endregion

        #region Plugin

        CreateMap<PluginCreateRequest, Plugin>();
        CreateMap<PluginUpdateRequest, Plugin>();
        CreateMap<Plugin, PluginDetailResponse>();

        #endregion

        #region Page

        CreateMap<PageCreateRequest, Page>();
        CreateMap<PageUpdateRequest, Page>();
        CreateMap<Page, PageDetailResponse>();

        #region PageFullDetailResponse
        CreateMap<(Page page,
        string path,
        Site site,
        IEnumerable<Layout> layouts,
        IEnumerable<Plugin> plugins,
        Dictionary<Guid, PluginDefinition> pluginDefinitions,
        bool forceMainSection),
        PageFullDetailResponse>()
            .ConvertUsing<PageFullDetailResponseTypeConverter>();
        #endregion

        #endregion

        #region Layout

        CreateMap<Layout, LayoutDetailResponse>();

        #endregion

        #region User

        CreateMap<UserCreateRequest, User>();
        CreateMap<UserUpdateRequest, User>();
        CreateMap<UserRegisterRequest, User>();
        CreateMap<User, UserDetailResponse>();

        #endregion

        #region App

        CreateMap<AppCreateRequest, App>();
        CreateMap<App, AppDetailResponse>();
        CreateMap<AppUpdateRequest, App>();

        #endregion

        #region Role

        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<Role, RoleDetailResponse>();

        #endregion

        #region ContentType

        CreateMap<ContentTypeCreateRequest, ContentType>();
        CreateMap<ContentTypeUpdateRequest, ContentType>();
        CreateMap<ContentType, ContentTypeDetailResponse>();

        #endregion

        #region Layout
        CreateMap<Layout, LayoutDetailResponse>();

        #endregion
    }
}
