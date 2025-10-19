namespace FluentCMS.Api.Plugins.IdentityManagement;

public class MappingProfile : Profile
{
    public MappingProfile()
    {

        #region User

        CreateMap<UserAddRequest, User>();
        CreateMap<UserUpdateRequest, User>();
        CreateMap<User, UserDto>();

        #endregion

        #region Role

        CreateMap<RoleAddRequest, Role>();
        CreateMap<RoleUpdateRequest, Role>();
        CreateMap<Role, RoleDto>();

        #endregion
    }
}
