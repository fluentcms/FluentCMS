using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;


public static partial class ApplicationContextDefaults
    {
    public static class Admins
        {

            public static readonly Role TestAdminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "TestAdminRole",
            };
            public static readonly User TestAdminUser = new User
            {
                //fill user data
                Id = Guid.NewGuid(),
                UserName = "TestAdmin",
                RoleIds = new() { TestAdminRole.Id },

            };
        }
    }


