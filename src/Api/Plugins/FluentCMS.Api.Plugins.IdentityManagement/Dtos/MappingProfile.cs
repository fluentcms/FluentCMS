namespace FluentCMS.Api.Plugins.IdentityManagement;

public class MappingProfile : Profile
{
    public MappingProfile()
    {

        #region User

        //CreateMap<UserCreateRequest, User>();
        //CreateMap<UserUpdateRequest, User>();
        //CreateMap<AccountUpdateRequest, User>();
        //CreateMap<UserRegisterRequest, User>();
        //CreateMap<User, UserDetailResponse>();

        #endregion

        #region Role

        CreateMap<RoleAddRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<Role, RoleDto>();

        #endregion
    }
}
