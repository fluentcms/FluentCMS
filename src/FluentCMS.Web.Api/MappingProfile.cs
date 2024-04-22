using FluentCMS.Web.Api.Models.Users;
using FluentCMS.Web.Api.ValueConverters;

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

        #region Content

        CreateMap<ContentCreateRequest, Content>().ForMember(x => x.Value,
            expression => expression.ConvertUsing(new ObjectDictionaryValueConverter()!));
        CreateMap<ContentUpdateRequest, Content>().ForMember(x => x.Value,
            expression => expression.ConvertUsing(new ObjectDictionaryValueConverter()!));
        CreateMap<Content, ContentDetailResponse>();

        #endregion
    }
}
