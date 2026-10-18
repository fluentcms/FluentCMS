using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FluentCMS.IntegrationTests.Audit;

public sealed class AuditEventsPostgresTests
{
    // The CI role is a superuser, which no revoke binds, so the migration is applied by a throwaway ordinary role that owns
    // its own database: the shape of a deployment whose host migrates at startup. Needs a connection that may create roles
    // and databases, which the documented local setup and CI both have.
    [PostgresFact]
    public async Task A_non_superuser_deploying_role_can_append_but_not_update_delete_or_truncate_audit_events()
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName)!);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = $"fluentcms_deployer_{suffix}";
        var databaseName = $"fluentcms_audit_{suffix}";
        var password = Guid.NewGuid().ToString("N");

        await ExecuteAsAdminAsync(admin, $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}'", $"CREATE DATABASE \"{databaseName}\" OWNER \"{role}\"");
        try
        {
            var deployer = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
            {
                Database = databaseName,
                Username = role,
                Password = password,
            };

            await using var services = TestServices.Build(
                new Dictionary<string, string?> { ["Database:ConnectionString"] = deployer.ConnectionString },
                collection => collection.AddScoped<ICurrentInitiator>(_ => new TestInitiator(new UserInitiator(Guid.NewGuid(), "Deployer"))));
            await TestServices.MigrateAsync(services);

            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

            await using (var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync())
            {
                await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(new NewAuditEvent("test.appended"));
                await work.CommitAsync();
            }

            Assert.Equal(1, await context.AuditEvents.CountAsync());

            await AssertInsufficientPrivilegeAsync(context, "UPDATE \"AuditEvents\" SET \"Operation\" = 'tampered'");
            await AssertInsufficientPrivilegeAsync(context, "DELETE FROM \"AuditEvents\"");
            await AssertInsufficientPrivilegeAsync(context, "TRUNCATE \"AuditEvents\"");

            Assert.Equal("test.appended", (await context.AuditEvents.SingleAsync()).Operation);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsAdminAsync(admin, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", $"DROP ROLE IF EXISTS \"{role}\"");
        }
    }

    private static async Task AssertInsufficientPrivilegeAsync(FluentCmsDbContext context, string statement)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(statement));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task ExecuteAsAdminAsync(NpgsqlConnectionStringBuilder admin, params string[] statements)
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();

        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
