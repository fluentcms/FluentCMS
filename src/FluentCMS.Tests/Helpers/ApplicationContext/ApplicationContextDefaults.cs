using FluentCMS.Entities;

namespace FluentCMS.Tests.Helpers.ApplicationContext;


    public static partial class ApplicationContextDefaults
    {
        public static readonly Host DefaultHost = new Host
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "TestSuperAdmin",
            LastUpdatedAt = DateTime.UtcNow,
            LastUpdatedBy = "TestSuperAdmin",
            SuperUsers = { "TestSuperAdmin" }
        };
        public static readonly Site DefaultSite = new()
        {
            Name = "DefaultSite",
            AdminRoleIds = new[] {Admins.TestAdminRole.Id, SuperAdmins.TestSuperAdminUser.Id }.ToList(),
        };
    }


