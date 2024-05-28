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

        #endregion

        #region Role

        CreateMap<RoleDetailResponse, RoleUpdateRequest>();

        #endregion
    }
}
