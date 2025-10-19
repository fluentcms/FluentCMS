using AutoMapper;
using IdentityExample.DTOs.Users;
using IdentityExample.DTOs.Admin;
using IdentityExample.DTOs.Roles;

namespace IdentityExample.Profiles;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        // User mappings
        CreateMap<User, UserProfileDto>();
        CreateMap<UpdateProfileRequest, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.IsSuperAdmin, opt => opt.Ignore())
            .ForMember(dest => dest.LastLogin, opt => opt.Ignore())
            .ForMember(dest => dest.LoginCount, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordChangedAt, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordChangedBy, opt => opt.Ignore())
            .ForMember(dest => dest.Suspended, opt => opt.Ignore());

        // Admin mappings
        CreateMap<User, AdminUserDto>();
        CreateMap<UpdateAdminUserRequest, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.IsSuperAdmin, opt => opt.Ignore())
            .ForMember(dest => dest.LastLogin, opt => opt.Ignore())
            .ForMember(dest => dest.LoginCount, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordChangedAt, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordChangedBy, opt => opt.Ignore())
            .ForMember(dest => dest.Suspended, opt => opt.Ignore());

        // Role mappings
        CreateMap<Role, RoleDto>();
        CreateMap<CreateRoleRequest, Role>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());
        CreateMap<UpdateRoleRequest, Role>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());
    }
}
