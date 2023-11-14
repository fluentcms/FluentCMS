using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;


    public static partial class ApplicationContextDefaults
    {
        public static class NonAdmins
        {

            public static readonly Role TestRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "TestRole",
            };
            public static readonly User TestUser = new User
            {
                //fill user data
                Id = Guid.NewGuid(),
                UserName = "Test",
                RoleIds = new() { TestRole.Id },

            };
        }
    }


