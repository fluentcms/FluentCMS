using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;

public static partial class ApplicationContextDefaults
{
    public static class Admins
    {
        public static readonly Role TestAdminRole = new()
        {
            Id = Guid.NewGuid(),
            Name = "TestAdminRole",
        };
        public static readonly User TestAdminUser = new()
        {
            Id = Guid.NewGuid(),
            UserName = "TestAdmin",
            RoleIds = [TestAdminRole.Id],
        };
    }
}
