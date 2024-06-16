using AutoMapper;

namespace FluentCMS.Web.ApiClients;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region Layout

        CreateMap<LayoutDetailResponse, LayoutUpdateRequest>();

        #endregion

        #region User

        CreateMap<UserDetailResponse, UserUpdateRequest>();
        CreateMap<UserDetailResponse, AccountUpdateRequest>();

        #endregion

        #region Role

        CreateMap<RoleDetailResponse, RoleUpdateRequest>();

        #endregion

        #region ApiToken

        CreateMap<ApiTokenDetailResponse, ApiTokenUpdateRequest>();

        #endregion

        #region ContentType

        CreateMap<ContentTypeDetailResponse, ContentTypeUpdateRequest>();

        #endregion

        #region Site

        CreateMap<SiteDetailResponse, SiteUpdateRequest>();

        #endregion

        #region Assets

        CreateMap<FolderDetailResponse, FolderUpdateRequest>();
        CreateMap<FileDetailResponse, FileUpdateRequest>();

        #endregion
    }
}
