using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;

public static partial class ApplicationContextDefaults
{
    public static class NonAdmins
    {
        public static readonly Role TestRole = new()
        {
            Id = Guid.NewGuid(),
            Name = "TestRole",
        };
        public static readonly User TestUser = new()
        {
            //fill user data
            Id = Guid.NewGuid(),
            UserName = "Test",
            RoleIds = [TestRole.Id],
        };
    }
}
