using FluentCMS.Web.Api.Models.Users;
using FluentCMS.Web.Api.ValueConverters;
using System.Text.Json;

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
        CreateMap<UserUpdateProfileRequest, User>();
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

        CreateMap<ContentTypeFieldSetRequest, ContentTypeField>()
            .ForMember(x => x.Metadata,
                expression => expression.ConvertUsing(new ObjectDictionaryValueConverter()))
            .ForMember(x => x.DefaultValue,
                expression => expression.ConvertUsing(new ObjectValueConverter()));
        CreateMap<ContentTypeField, ContentTypeFieldResponse>();

        #region Content

        CreateMap<ContentCreateRequest, Content>().ForMember(x => x.Value,
            expression => expression.ConvertUsing(new ObjectDictionaryValueConverter()));
        CreateMap<ContentUpdateRequest, Content>().ForMember(x => x.Value,
            expression => expression.ConvertUsing(new ObjectDictionaryValueConverter()));
        CreateMap<Content, ContentDetailResponse>();

        #endregion


    }
}
