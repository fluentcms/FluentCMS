using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Web.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
