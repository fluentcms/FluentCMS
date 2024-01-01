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
        CreateMap<Page, PageFullDetailResponse>();

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
        CreateMap<App, AppResponse>();
        CreateMap<AppUpdateRequest, App>();

        #endregion
    }
}
