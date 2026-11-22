using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Administration.Jobs;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the jobs area held to the reference host's OpenAPI document (spec 7), and its calls on the test host: a job
// that fails is dead-lettered with its attempt and retried, a pending one is cancelled into the dead letters, the failed
// event deliveries are read and their routes refuse a delivery that is not there, and a caller who holds jobs.manage through
// a role of a site is refused every call.
public sealed class JobsContractTests
{
    private const string Prefix = "/api/management/v1/";
    private const string FailingType = "admin-tests.failing";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheJobsAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = JobsJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method) =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method), $"{path} {method}", document, gaps);

        Check(records.JobRecordPage, "jobs", "get");
        Check(records.JobDetailRecord, "jobs/{id}", "get");
        Check(records.JobDetailRecord, "jobs/{id}/retry", "post");
        Check(records.JobDetailRecord, "jobs/{id}/cancel", "post");
        Check(records.ItemListDeadJobRecord, "jobs/dead-letter", "get");
        Check(records.ItemListFailedDeliveryRecord, "events/dead-letter", "get");
        Check(records.PurgedRecord, "events/dead-letter", "delete");

        gaps.ShouldBeEmpty();
        paths[Prefix + "events/dead-letter/{id}/retry"]?["post"]?["responses"]?["204"].ShouldNotBeNull();
        paths[Prefix + "events/dead-letter/{id}"]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
    }

    [Fact]
    public async Task AFailedJobIsDeadLetteredWithItsAttemptAndRetriedAndAPendingOneIsCancelled()
    {
        await using var admin = new AdminHost();
        admin.Host.Configure(cms => cms.Jobs.AddJob<FailingJob, FailingPayload>(FailingType));
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;
        var scheduler = admin.Host.Services.GetRequiredService<IJobScheduler>();

        var failing = await scheduler.EnqueueAsync<FailingJob, FailingPayload>(new FailingPayload("boom"), new JobOptions { MaxAttempts = 1 }, Token);
        var later = await scheduler.ScheduleAsync<FailingJob, FailingPayload>(new FailingPayload("later"), DateTimeOffset.UtcNow.AddDays(1), cancellationToken: Token);

        var dead = await DeadLetteredAsync(api, failing);
        dead.History.ShouldHaveSingleItem().Outcome.ShouldBe("failed");
        dead.History[0].Error.ShouldNotBeNullOrEmpty();
        (await api.GetDeadJobsAsync(1, Token)).ShouldContain(job => job.Id == failing && job.Type == FailingType);
        var query = new RecordQuery(null, 1, 25) { Filters = new Dictionary<string, string> { [JobsApi.TypeFilter] = FailingType, [JobsApi.StatusFilter] = "pending" } };
        (await api.GetJobsAsync(query, Token)).Items.Select(job => job.Id).ShouldBe([later]);

        var retried = await api.RetryJobAsync(failing, Token);
        (retried.Status is "pending" or "running" or "dead_lettered").ShouldBeTrue();
        retried.MaxAttempts.ShouldBeGreaterThan(1);

        var cancelled = await api.CancelJobAsync(later, Token);
        (cancelled.Status, cancelled.Type).ShouldBe(("dead_lettered", FailingType));
        JobProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.CancelJobAsync(later, Token)), JobProblems.NotCancellable).ShouldBeTrue();
        JobProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.GetJobAsync(Guid.NewGuid(), Token)), JobProblems.JobNotFound).ShouldBeTrue();
        JobProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RetryJobAsync(Guid.NewGuid(), Token)), JobProblems.JobNotFound).ShouldBeTrue();
    }

    [Fact]
    public async Task TheFailedEventDeliveriesAreReadAndADeliveryThatIsNotThereIsRefused()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        (await api.GetFailedDeliveriesAsync(1, Token)).ShouldBeEmpty();
        var purged = await api.PurgeDeliveriesAsync("admin-tests.no-such-handler", Token);
        (purged.Handler, purged.Removed).ShouldBe(("admin-tests.no-such-handler", 0));
        JobProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RetryDeliveryAsync(Guid.NewGuid(), Token)), JobProblems.DeliveryNotFound).ShouldBeTrue();
        JobProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RemoveDeliveryAsync(Guid.NewGuid(), Token)), JobProblems.DeliveryNotFound).ShouldBeTrue();
    }

    // Every route of the area is for the instance, so the shell hides the area from such a caller (AQ22).
    [Fact]
    public async Task ACallerWhoHoldsJobsManageThroughARoleOfASiteIsRefusedEveryCall()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Site operator",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.JobsManage }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Site operator");
        var api = client.Api;

        foreach (var call in (Func<Task>[])
        [
            () => api.GetJobsAsync(new RecordQuery(null, 1, 25), Token),
            () => api.GetDeadJobsAsync(1, Token),
            () => api.GetFailedDeliveriesAsync(1, Token),
            () => api.PurgeDeliveriesAsync("admin-tests.handler", Token),
        ])
        {
            (await Should.ThrowAsync<ApiProblemException>(call)).Problem.Status.ShouldBe(403);
        }
    }

    // The runner works the job at once, and dead-letters it after its one attempt.
    private static async Task<JobDetailRecord> DeadLetteredAsync(IManagementApi api, Guid id)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var job = await api.GetJobAsync(id, bound.Token);
            if (job.Status == "dead_lettered")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
        }
    }

    public sealed record FailingPayload(string Reason);

    // A job that never succeeds, for the dead letters.
    public sealed class FailingJob : IJob<FailingPayload>
    {
        public Task ExecuteAsync(FailingPayload payload, JobContext context)
        {
            ArgumentNullException.ThrowIfNull(payload);
            throw new InvalidOperationException(payload.Reason);
        }
    }
}
