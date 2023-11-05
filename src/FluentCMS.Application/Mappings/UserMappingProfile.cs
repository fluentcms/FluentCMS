using AutoMapper;
using FluentCMS.Application.Dtos.Users;
using FluentCMS.Entities.Users;

namespace FluentCMS.Application.Mappings;
internal class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(x => x.UserRoles, cfg => cfg.MapFrom(y => y.UserRoles.Select(z => z.RoleId.ToString())));

        CreateMap<Role, RoleDto>();
    }
}
