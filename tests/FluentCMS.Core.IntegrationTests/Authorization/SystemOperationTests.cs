using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Seeding;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using ApiKeyClass = FluentCMS.Core.Identity.ApiKeys.ApiKeyClass;

namespace FluentCMS.Core.IntegrationTests.Authorization;

// The permission checks of the Management services refuse code that runs outside an operation; jobs and seeders run as
// the system, so they pass every check without asking for it (#2852).
public sealed class SystemOperationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHostsOwnCodeOutsideAnOperationIsRefused()
    {
        await using var host = new FluentCmsTestHost();
        await using var scope = host.Services.CreateAsyncScope();

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IPermissionChecker>().AllowsAsync(CorePermissions.ApiKeysManage, site: null, Token).AsTask());
        refused.Message.ShouldStartWith(AuthorizationErrorCodes.NoOperation + ":");
        refused.Message.ShouldContain("SystemOperation.Begin");

        await Should.ThrowAsync<InvalidOperationException>(() => CreateKeyAsync(scope.ServiceProvider, "outside"));
        (await KeyExistsAsync(host, "outside")).ShouldBeFalse();
    }

    [Fact]
    public async Task AJobPassesTheChecksOfTheManagementServices()
    {
        await using var host = new FluentCmsTestHost().Configure(cms =>
        {
            cms.Jobs.Options.PollInterval = TimeSpan.FromDays(1);
            cms.Jobs.AddJob<ManagingJob, ManagingPayload>(ManagingJob.Type);
        });

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueueAsync<ManagingJob, ManagingPayload>(new ManagingPayload("from a job"), cancellationToken: Token);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token)).ShouldBeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().SingleAsync(job => job.Id == id, Token)).Status.ShouldBe(JobStatus.Succeeded);
        }

        (await KeyExistsAsync(host, "from a job")).ShouldBeTrue();
    }

    [Fact]
    public async Task ASeederPassesTheChecksOfTheManagementServices()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.Seeding.Add<ManagingSeeder>());

        (await KeyExistsAsync(host, ManagingSeeder.KeyName)).ShouldBeTrue();
    }

    private static async Task CreateKeyAsync(IServiceProvider services, string name)
    {
        await services.GetRequiredService<IPermissionChecker>().RequireAsync(CorePermissions.ApiKeysManage, site: null);
        await services.GetRequiredService<ApiKeyService>().CreateAsync(new ApiKeyRequest(name, ApiKeyClass.Delivery, BuiltInRoles.Public));
    }

    private static async Task<bool> KeyExistsAsync(FluentCmsTestHost host, string name)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ApiKey>().AnyAsync(key => key.Name == name, Token);
    }

    public sealed record ManagingPayload(string Name);

    public sealed class ManagingJob(IServiceProvider services) : IJob<ManagingPayload>
    {
        public const string Type = "test.managing";

        public Task ExecuteAsync(ManagingPayload payload, JobContext context) => CreateKeyAsync(services, payload.Name);
    }

    public sealed class ManagingSeeder(IServiceProvider services) : ISeeder
    {
        public const string KeyName = "from a seeder";

        public Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default) => CreateKeyAsync(services, KeyName);
    }
}
