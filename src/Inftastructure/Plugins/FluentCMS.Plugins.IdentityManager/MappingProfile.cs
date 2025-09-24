namespace FluentCMS.Plugins.IdentityManager;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Role, RoleResponse>();
        CreateMap<RoleRequest, Role>();
    }

}