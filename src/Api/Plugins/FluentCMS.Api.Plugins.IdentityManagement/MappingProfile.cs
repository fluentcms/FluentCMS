namespace FluentCMS.Api.Plugins.IdentityManagement;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Role, RoleResponse>();
        CreateMap<RoleRequest, Role>();
    }

}
