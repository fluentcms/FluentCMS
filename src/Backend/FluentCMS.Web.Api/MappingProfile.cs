using FluentCMS.Services.Models;

namespace FluentCMS.Web.Api;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region Setup

        CreateMap<SetupRequest, SetupTemplate>();

        #endregion

        #region Site

        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();
        CreateMap<Site, SiteDetailResponse>();
        CreateMap<SiteCreateRequest, SiteTemplate>();

        #endregion

        #region PluginDefinition

        CreateMap<PluginDefinitionCreateRequest, PluginDefinition>();
        CreateMap<PluginDefinition, PluginDefinitionDetailResponse>();

        #endregion

        #region Plugin

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
        CreateMap<LayoutCreateRequest, Layout>();
        CreateMap<LayoutUpdateRequest, Layout>();

        #endregion

        #region User

        CreateMap<UserCreateRequest, User>();
        CreateMap<UserUpdateRequest, User>();
        CreateMap<AccountUpdateRequest, User>();
        CreateMap<UserRegisterRequest, User>();
        CreateMap<User, UserDetailResponse>();

        #endregion

        #region UserRole

        CreateMap<IApiExecutionContext, UserRoleDetailResponse>();

        #endregion

        #region Role

        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<Role, RoleDetailResponse>();

        #endregion

        #region API Token

        CreateMap<ApiTokenCreateRequest, ApiToken>();
        CreateMap<ApiTokenUpdateRequest, ApiToken>();
        CreateMap<ApiToken, ApiTokenDetailResponse>();

        #endregion

        #region ContentType

        CreateMap<ContentTypeCreateRequest, ContentType>();
        CreateMap<ContentTypeUpdateRequest, ContentType>();
        CreateMap<ContentType, ContentTypeDetailResponse>();

        #endregion

        #region Content

        CreateMap<Content, ContentDetailResponse>();

        #endregion

        #region File Folder Asset

        CreateMap<File, FileDetailResponse>();
        CreateMap<FileUpdateRequest, File>();
        CreateMap<Folder, FolderDetailResponse>();
        CreateMap<FolderCreateRequest, Folder>();
        CreateMap<FolderUpdateRequest, Folder>();

        #endregion

        #region PluginContent

        CreateMap<PluginContent, PluginContentDetailResponse>();

        #endregion

        #region GlobalSetting

        CreateMap<GlobalSettings, GlobalSettingsResponse>();
        CreateMap<GlobalSettingsUpdateRequest, GlobalSettings>();

        #endregion

        #region Block

        CreateMap<BlockCreateRequest, Block>();
        CreateMap<BlockUpdateRequest, Block>();
        CreateMap<Block, BlockDetailResponse>();

        #endregion
    }
}
