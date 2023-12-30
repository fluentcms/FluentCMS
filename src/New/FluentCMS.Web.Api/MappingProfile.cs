namespace FluentCMS.Web.Api;
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region User

        CreateMap<UserCreateRequest, User>();
        CreateMap<User, UserResponse>();
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
