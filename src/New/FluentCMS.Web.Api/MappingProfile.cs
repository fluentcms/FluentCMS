using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Web.Api.Models;

namespace FluentCMS.Web.Api;
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region User

        CreateMap<UserCreateRequest, User>();
        CreateMap<User, UserResponse>();
        CreateMap<UserUpdateRequest, User>();

        #endregion
    }
}
