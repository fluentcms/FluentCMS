namespace FluentCMS.Web.Api;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region Site

        CreateMap<SiteCreateRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();
        CreateMap<Site, SiteDetailResponse>();

        #endregion

        #region PluginDefinition

        CreateMap<PluginDefinitionCreateRequest, PluginDefinition>();
        CreateMap<PluginDefinition, PluginDefinitionDetailResponse>();
        CreateMap<Entities.PluginDefinitionType, Models.PluginDefinitionType>();
        CreateMap<Models.PluginDefinitionType, Entities.PluginDefinitionType>();

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

        #region Role

        CreateMap<RoleCreateRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<Role, RoleDetailResponse>();

        #endregion

        #region API Token

        CreateMap<ApiTokenCreateRequest, ApiToken>();
        CreateMap<ApiToken, ApiTokenDetailResponse>();
        CreateMap<Entities.Policy, Models.Policy>();
        CreateMap<Models.Policy, Entities.Policy>();

        #endregion

        #region ContentType

        CreateMap<ContentTypeCreateRequest, ContentType>();
        CreateMap<ContentTypeUpdateRequest, ContentType>();
        CreateMap<ContentType, ContentTypeDetailResponse>();
        CreateMap<Entities.ContentTypeField, Models.ContentTypeField>();

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

        #region Global Setting

        CreateMap<GlobalSettingsUpdateRequest, GlobalSettings>();
        CreateMap<Entities.FileUploadConfiguration, Models.FileUploadConfiguration>();
        CreateMap<Models.FileUploadConfiguration, Entities.FileUploadConfiguration>();
        CreateMap<Entities.SmtpServerConfiguration, Models.SmtpServerConfiguration>();
        CreateMap<Models.SmtpServerConfiguration, Entities.SmtpServerConfiguration>();

        #endregion

    }
}
