using AutoMapper;

namespace FluentCMS.Web.UI;

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
    }
}
