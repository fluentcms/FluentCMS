using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;

public static partial class ApplicationContextDefaults
{
    public static class SuperAdmins
    {
        public static readonly Role TestSuperAdminRole = new()
        {
            Id = Guid.NewGuid(),
            Name = "TestSuperAdminRole",
        };
        public static readonly User TestSuperAdminUser = new()
        {
            //fill user data
            Id = Guid.NewGuid(),
            UserName = "TestSuperAdmin",
            RoleIds = [TestSuperAdminRole.Id],
        };
    }
}
