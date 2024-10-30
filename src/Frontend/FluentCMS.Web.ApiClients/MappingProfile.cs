using AutoMapper;

namespace FluentCMS.Web.ApiClients;

// TODO: Move to a its own plugin projects
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region Block

        CreateMap<BlockDetailResponse, BlockUpdateRequest>();

        #endregion

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

        #region Page

        CreateMap<PageDetailResponse, PageUpdateRequest>();

        #endregion

        #region File Folder

        CreateMap<FileDetailResponse, FileUpdateRequest>();

        #endregion
    }
}
