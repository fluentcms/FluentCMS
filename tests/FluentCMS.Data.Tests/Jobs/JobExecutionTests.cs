using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

// One step at a time on a manual clock, so that what happens is what the test does and nothing else is running.
public sealed class JobExecutionTests : IDisposable
{
    private const string Queue = JobRunnerOptions.DefaultQueue;
    private const string Owner = "worker";
    private const string OtherOwner = "other-worker";

    private static readonly TimeSpan Lease = new JobRunnerOptions().LeaseDuration;

    private readonly JobFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private Task<bool> RunNextAsync(string owner = Owner) => _fixture.Executor.RunNextAsync(Queue, owner, TestContext.Current.CancellationToken);

    private void AlwaysFail() =>
        _fixture.Probe.Behavior = (_, _) => throw new InvalidOperationException("It broke.");

    [Fact]
    public async Task AJobRunsOnceAndSucceeds()
    {
        var id = await _fixture.EnqueueAsync();

        (await RunNextAsync()).ShouldBeTrue();
        (await RunNextAsync()).ShouldBeFalse();

        _fixture.Probe.Runs.Count.ShouldBe(1);
        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Succeeded);
        job.Attempts.ShouldBe(1);
        job.CompletedAt.ShouldBe(JobFixture.Start);
        job.LeaseOwner.ShouldBeNull();
        job.LeaseUntil.ShouldBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
        _fixture.Events.Failed.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetriesBackOffThenDeadLetterWithEveryAttemptRecorded()
    {
        AlwaysFail();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 3 });

        (await RunNextAsync()).ShouldBeTrue();
        var first = _fixture.Job(id);
        first.Status.ShouldBe(JobStatus.Pending);
        first.LastError.ShouldNotBeNull().ShouldContain("It broke.");
        first.RunAt.ShouldBeInRange(JobFixture.Start.AddSeconds(30), JobFixture.Start.AddSeconds(36));

        (await RunNextAsync()).ShouldBeFalse("the retry is not due yet");

        _fixture.Clock.Advance(first.RunAt - _fixture.Clock.GetUtcNow());
        (await RunNextAsync()).ShouldBeTrue();
        var second = _fixture.Job(id);
        second.RunAt.ShouldBeInRange(first.RunAt.AddSeconds(60), first.RunAt.AddSeconds(72));

        _fixture.Clock.Advance(second.RunAt - _fixture.Clock.GetUtcNow());
        (await RunNextAsync()).ShouldBeTrue();

        var last = _fixture.Job(id);
        last.Status.ShouldBe(JobStatus.DeadLettered);
        last.CompletedAt.ShouldBe(second.RunAt);
        last.LeaseOwner.ShouldBeNull();
        _fixture.Probe.Runs.Count.ShouldBe(3);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed), (2, JobAttemptOutcome.Failed), (3, JobAttemptOutcome.Failed));
        _fixture.Attempts(id).ShouldAllBe(attempt => attempt.FinishedAt != null && attempt.Error!.Contains("It broke."));

        _fixture.Events.Failed.Select(failed => failed.Attempt).ShouldBe([1, 2, 3]);
        _fixture.Events.DeadLettered.ShouldHaveSingleItem().JobId.ShouldBe(id);

        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        (await RunNextAsync()).ShouldBeFalse("a dead-lettered job does not come back by itself");
    }

    [Fact]
    public async Task EachRetryFromDeadLetterGivesTheDefaultAttempts()
    {
        AlwaysFail();
        var further = new JobRunnerOptions().DefaultMaxAttempts;
        var letters = _fixture.Services.GetRequiredService<IJobDeadLetters>();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 2 });

        var deadAt = 2;
        for (var round = 0; round < 3; round++)
        {
            while (_fixture.Job(id).Status != JobStatus.DeadLettered)
            {
                _fixture.Clock.Advance(TimeSpan.FromHours(2));
                await RunNextAsync();
            }

            _fixture.Job(id).Attempts.ShouldBe(deadAt);

            (await letters.RetryAsync(id, TestContext.Current.CancellationToken)).ShouldBeTrue();

            deadAt += further;
            _fixture.Job(id).MaxAttempts.ShouldBe(deadAt);
        }
    }

    [Fact]
    public void BackoffIsCappedHoweverManyAttemptsThereHaveBeen()
    {
        var options = new JobRunnerOptions();

        RetryBackoff.After(int.MaxValue, options).ShouldBeLessThanOrEqualTo(options.RetryMaxDelay * (1 + options.RetryJitter));
        RetryBackoff.After(1, options).ShouldBeGreaterThanOrEqualTo(options.RetryBaseDelay);
    }

    [Fact]
    public async Task ADeadLetteredJobIsListedAndCanBeRetried()
    {
        AlwaysFail();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 }, secret: "payload-stays-out");
        await RunNextAsync();

        var letters = _fixture.Services.GetRequiredService<IJobDeadLetters>();
        var token = TestContext.Current.CancellationToken;

        var listed = (await letters.ListAsync(0, 50, token)).ShouldHaveSingleItem();
        listed.Id.ShouldBe(id);
        listed.Type.ShouldBe(JobFixture.ProbeType);
        listed.Attempts.ShouldBe(1);
        listed.LastError.ShouldNotBeNull().ShouldContain("It broke.");
        listed.DeadLetteredAt.ShouldBe(JobFixture.Start);
        (await letters.ListAsync(1, 50, token)).ShouldBeEmpty();

        (await letters.RetryAsync(Guid.CreateVersion7(), token)).ShouldBeFalse();
        (await letters.RetryAsync(id, token)).ShouldBeTrue();
        (await letters.RetryAsync(id, token)).ShouldBeFalse("it is no longer dead-lettered");
        (await letters.ListAsync(0, 50, token)).ShouldBeEmpty();

        _fixture.Probe.Behavior = (_, _) => Task.CompletedTask;
        (await RunNextAsync()).ShouldBeTrue();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Succeeded);
        job.Attempts.ShouldBe(2);
        job.CompletedAt.ShouldNotBeNull();
        job.LastError.ShouldBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task AKilledWorkersJobReturnsAfterItsLeaseExpires()
    {
        var id = await _fixture.EnqueueAsync();

        // The worker claims the job and is never heard from again.
        var claimed = (await _fixture.Store.ClaimAsync(Queue, Owner, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        (await RunNextAsync(OtherOwner)).ShouldBeFalse("the lease still holds");

        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));
        (await RunNextAsync(OtherOwner)).ShouldBeTrue();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Succeeded);
        job.Attempts.ShouldBe(2);
        _fixture.Probe.Runs.Count.ShouldBe(1);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
        _fixture.Events.Failed.ShouldHaveSingleItem().Attempt.ShouldBe(1);

        // If that worker was only stalled, what it writes on waking changes nothing.
        (await _fixture.Store.FailAsync(claimed, "too late")).ShouldBeFalse();
        (await _fixture.Store.SucceedAsync(claimed)).ShouldBeFalse();
        (await _fixture.Store.RenewLeaseAsync(claimed)).ShouldBeNull();
        _fixture.Job(id).LastError.ShouldBeNull();
        _fixture.Attempts(id)[0].Outcome.ShouldBe(JobAttemptOutcome.LeaseExpired);
    }

    [Fact]
    public async Task AWorkerThatDiedOnTheLastAttemptIsDeadLetteredByTheSweep()
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });
        (await _fixture.Store.ClaimAsync(Queue, Owner, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        (await _fixture.Store.SweepAsync()).ShouldBe(0, "the lease still holds");

        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));
        (await RunNextAsync(OtherOwner)).ShouldBeFalse("no claim takes a job with no attempts left");
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running);

        (await _fixture.Store.SweepAsync()).ShouldBe(1);

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.DeadLettered);
        job.LeaseOwner.ShouldBeNull();
        job.LeaseUntil.ShouldBeNull();
        job.CompletedAt.ShouldBe(_fixture.Clock.GetUtcNow());
        job.LastError.ShouldNotBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired));
        _fixture.Events.DeadLettered.ShouldHaveSingleItem().JobId.ShouldBe(id);

        (await _fixture.Store.SweepAsync()).ShouldBe(0);
        _fixture.Events.DeadLettered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheSweepLeavesAJobThatStillHasAttempts()
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 2 });
        await _fixture.Store.ClaimAsync(Queue, Owner, TestContext.Current.CancellationToken);

        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));

        (await _fixture.Store.SweepAsync()).ShouldBe(0);
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running);
    }

    [Fact]
    public async Task ConcurrentSweepsDeadLetterAJobOnce()
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });
        await _fixture.Store.ClaimAsync(Queue, Owner, TestContext.Current.CancellationToken);
        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));

        var stores = Enumerable.Range(0, 4).Select(_ => _fixture.NewInstance().GetRequiredService<JobStore>()).ToList();
        var swept = await Task.WhenAll(stores.Select(store => Task.Run(store.SweepAsync, TestContext.Current.CancellationToken)));

        swept.Sum().ShouldBe(1);
        _fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);
        _fixture.Events.DeadLettered.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AScheduledJobWaitsForItsRunAt()
    {
        var runAt = JobFixture.Start.AddHours(1);
        var id = await _fixture.Scheduler.ScheduleAsync<ProbeJob, ProbePayload>(new ProbePayload("later"), runAt, cancellationToken: TestContext.Current.CancellationToken);

        (await RunNextAsync()).ShouldBeFalse();

        _fixture.Clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
        (await RunNextAsync()).ShouldBeFalse();

        _fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await RunNextAsync()).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task HigherPriorityRunsFirst()
    {
        await _fixture.EnqueueAsync("normal");
        await _fixture.EnqueueAsync("low", new JobOptions { Priority = -1 });
        await _fixture.EnqueueAsync("high", new JobOptions { Priority = 5 });

        while (await RunNextAsync())
        {
        }

        _fixture.Probe.Runs.Select(run => run.Payload.Key).ShouldBe(["high", "normal", "low"]);
    }

    [Fact]
    public async Task AWorkerTakesOnlyItsOwnQueue()
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { Queue = "mail" });

        (await RunNextAsync()).ShouldBeFalse();
        (await _fixture.Executor.RunNextAsync("mail", Owner, TestContext.Current.CancellationToken)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task AnUnknownTypeFailsTheAttemptWithAClearError()
    {
        var id = _fixture.AddRow("gone-with-a-plugin", "{}", maxAttempts: 1);

        (await RunNextAsync()).ShouldBeTrue();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.DeadLettered);
        job.LastError.ShouldBe("FCMS2501: no job is registered for the type 'gone-with-a-plugin'.");
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
    }

    [Theory]
    [InlineData("""{"key":5,"secret":"secret-in-a-bad-payload"}""")]
    [InlineData("""{"key":"a","actor":{"kind":"secret-in-a-bad-payload"}}""")]
    [InlineData("""["secret-in-a-bad-payload"]""")]
    [InlineData("null")]
    public async Task APayloadThatDoesNotDeserializeFailsTheAttemptWithAClearError(string payload)
    {
        Assert.SkipWhen(TestProvider.IsSqlServer && payload == "null", "SQL Server's ISJSON check refuses a payload that is not an object or an array, so no such row can be stored.");
        var id = _fixture.AddRow(JobFixture.ProbeType, payload, maxAttempts: 1);

        (await RunNextAsync()).ShouldBeTrue();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.DeadLettered);
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2502");
        job.LastError.ShouldNotContain("secret-in-a-bad-payload");
        _fixture.Probe.Runs.ShouldBeEmpty();
        _fixture.Attempts(id).ShouldHaveSingleItem().Error.ShouldBe(job.LastError);
    }

    // SQL Server's ISJSON check refuses a bare scalar, so one is stored in an object, and the job still gets its value.
    [Fact]
    public async Task AScalarPayloadIsStoredInAnObjectAndTheJobGetsItsValue()
    {
        var seen = new ConcurrentQueue<object>();
        var instance = _fixture.NewInstance(cms: builder =>
        {
            builder.Services.AddSingleton(seen);
            builder.Jobs.AddJob<ScalarJob<int>, int>("scalar-number");
            builder.Jobs.AddJob<ScalarJob<string>, string>("scalar-text");
        });
        var scheduler = instance.GetRequiredService<IJobScheduler>();

        var number = await scheduler.EnqueueAsync<ScalarJob<int>, int>(12, cancellationToken: TestContext.Current.CancellationToken);
        var text = await scheduler.EnqueueAsync<ScalarJob<string>, string>("twelve", cancellationToken: TestContext.Current.CancellationToken);

        JsonNode.Parse(_fixture.Job(number).Payload)!["value"]!.GetValue<int>().ShouldBe(12);
        JsonNode.Parse(_fixture.Job(text).Payload)!["value"]!.GetValue<string>().ShouldBe("twelve");

        var executor = instance.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(Queue, Owner, TestContext.Current.CancellationToken))
        {
        }

        seen.ShouldBe([12, "twelve"], ignoreOrder: true);
        _fixture.Job(number).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.Job(text).Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task AJobRunsAsTheSystemPrincipalWithOriginJob()
    {
        var id = await _fixture.EnqueueAsync();

        await RunNextAsync();

        var run = _fixture.Probe.Runs.ShouldHaveSingleItem();
        run.Ambient.ShouldBeSameAs(run.Context.Operation);
        run.Context.JobId.ShouldBe(id);
        run.Context.Type.ShouldBe(JobFixture.ProbeType);
        run.Context.Queue.ShouldBe(Queue);
        run.Context.Attempt.ShouldBe(1);
        run.Context.MaxAttempts.ShouldBe(new JobRunnerOptions().DefaultMaxAttempts);

        var operation = run.Context.Operation;
        operation.Principal.ShouldBeSameAs(Principal.System);
        operation.Origin.ShouldBe(Origin.Job);
        operation.Actor.ShouldBeNull();
        operation.AuditPrincipal.ShouldBeSameAs(Principal.System);
        SystemOperation.Allows(operation.Origin).ShouldBeTrue();
        operation.Site.ShouldBe(new SiteId(DefaultSite.Id));
        operation.Locale.ShouldBe(DefaultSite.Locale);
        operation.CorrelationId.ShouldBe(id.ToString());
        operation.Timestamp.ShouldBe(JobFixture.Start);

        _fixture.Services.GetRequiredService<IOperationContextAccessor>().Current.ShouldBeNull();
    }

    [Fact]
    public async Task AnActorInThePayloadIsForAuditAndGivesTheJobNothing()
    {
        var actor = new JobActor(PrincipalKind.AdminUser, Guid.CreateVersion7(), "Scheduler");
        await _fixture.EnqueueAsync(actor: actor);

        await RunNextAsync();

        var operation = _fixture.Probe.Runs.ShouldHaveSingleItem().Context.Operation;
        operation.Principal.ShouldBeSameAs(Principal.System);
        operation.Origin.ShouldBe(Origin.Job);
        var audit = operation.AuditPrincipal;
        audit.ShouldBeSameAs(operation.Actor);
        audit.Kind.ShouldBe(PrincipalKind.AdminUser);
        audit.Id.ShouldBe(actor.Id);
        audit.Name.ShouldBe("Scheduler");
    }

    // The payload has no way to bring claims, however it is written, and claims are where roles and scopes come from.
    [Fact]
    public async Task AnActorCannotCarryClaims()
    {
        var id = Guid.CreateVersion7();
        _fixture.AddRow(JobFixture.ProbeType, """{"actor":{"kind":1,"id":"ID","name":"x","claims":{"role":"Super Admin"}}}""".Replace("ID", id.ToString(), StringComparison.Ordinal));

        await RunNextAsync();

        var actor = _fixture.Probe.Runs.ShouldHaveSingleItem().Context.Operation.Actor.ShouldNotBeNull();
        actor.Id.ShouldBe(id);
        actor.Claims.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PrincipalKind.System)]
    [InlineData(PrincipalKind.Anonymous)]
    public async Task AnActorMayNotBeAReservedPrincipal(PrincipalKind kind)
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 }, actor: new JobActor(kind, Guid.CreateVersion7(), "forged"));

        await RunNextAsync();

        _fixture.Probe.Runs.ShouldBeEmpty();
        _fixture.Job(id).LastError.ShouldNotBeNull().ShouldStartWith("FCMS2507");
    }

    [Fact]
    public async Task ASiteJobRunsInThatSiteWithItsDefaultLocale()
    {
        var site = new Site { Name = "Fr", Slug = "fr", DefaultLocale = "fr", CreatedAt = JobFixture.Start };
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            context.Add(site);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _fixture.EnqueueAsync(options: new JobOptions { Site = new SiteId(site.Id) });
        await RunNextAsync();

        var operation = _fixture.Probe.Runs.ShouldHaveSingleItem().Context.Operation;
        operation.Site.ShouldBe(new SiteId(site.Id));
        operation.Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task EnqueuingRecordsWhoAndWhereFromTheCurrentOperation()
    {
        var user = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "Ann");
        var site = new SiteId(Guid.CreateVersion7());
        var accessor = _fixture.Services.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(user, Origin.Admin, site, "en", null, "c", JobFixture.Start);

        var id = await _fixture.EnqueueAsync();
        accessor.Current = null;

        var job = _fixture.Job(id);
        job.CreatedBy.ShouldBe(user.Id);
        job.Origin.ShouldBe(Origin.Admin);
        job.SiteId.ShouldBe(site.Value);
        job.CreatedAt.ShouldBe(JobFixture.Start);
    }

    [Fact]
    public async Task EnqueuingOutsideAnOperationRecordsNoOne()
    {
        var job = _fixture.Job(await _fixture.EnqueueAsync());

        job.CreatedBy.ShouldBeNull();
        job.Origin.ShouldBe(Origin.Job);
        job.SiteId.ShouldBeNull();
        job.Queue.ShouldBe(Queue);
    }

    [Fact]
    public async Task AnUnregisteredJobIsRefusedWhenEnqueued()
    {
        var failure = await Should.ThrowAsync<FluentCmsConfigurationException>(() =>
            _fixture.Scheduler.EnqueueAsync<UnregisteredJob, ProbePayload>(new ProbePayload(), cancellationToken: TestContext.Current.CancellationToken));

        failure.Code.ShouldBe("FCMS2504");
    }

    [Fact]
    public async Task ANullPayloadAndNoAttemptsAreRefusedWhenEnqueued()
    {
        var token = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<ArgumentNullException>(() => _fixture.Scheduler.EnqueueAsync<ProbeJob, ProbePayload>(null!, cancellationToken: token));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 0 }));
    }

    [Fact]
    public void TwoJobsCannotShareAType()
    {
        using var database = TestDatabase.Create();
        using var services = database.CreateServices(configure: cms => cms.Jobs
            .AddJob<ProbeJob, ProbePayload>("same")
            .AddJob<UnregisteredJob, ProbePayload>("same"));

        Should.Throw<FluentCmsConfigurationException>(services.GetRequiredService<IJobScheduler>).Code.ShouldBe("FCMS2505");
    }

    [Fact]
    public async Task AFailureThatQuotesThePayloadIsNotStoredWithIt()
    {
        const string Secret = "api-key-1234567890";
        _fixture.Probe.Behavior = (payload, _) =>
            throw new InvalidOperationException($"POST failed with key {payload.Secret}", new FormatException($"bad {payload.Secret}"));
        var id = await _fixture.EnqueueAsync(secret: Secret);

        await RunNextAsync();

        var job = _fixture.Job(id);
        job.LastError.ShouldBe("System.InvalidOperationException: POST failed with *** *** ---> System.FormatException: bad ***");
        _fixture.Attempts(id).ShouldHaveSingleItem().Error.ShouldBe(job.LastError);
        _fixture.Events.Failed.ShouldHaveSingleItem().Error.ShouldBe(job.LastError);
    }

    [Fact]
    public void ScrubbingTakesOutNumbersShortStringsAndPropertyNames()
    {
        const string Payload = """{"card":4111111111111111,"pin":"123","tokens":{"sk_live_abcdef":1.50},"secret":"hunter2hunter2"}""";
        var failure = new InvalidOperationException("number 4111111111111111 code 123 token sk_live_abcdef rate 1.50 secret hunter2hunter2 rejected");

        var text = JobErrorText.ForStorage(failure, Payload);

        text.ShouldBe("System.InvalidOperationException: number *** code *** token *** rate *** *** *** rejected");
        JobErrorText.ForLog(failure, Payload).ShouldNotContain("4111111111111111");
    }

    [Fact]
    public void TheNamesOfTheSchemasTypesAndFieldsAreKept()
    {
        var schema = SampleSchema.NewBuilder()
            .AddBlock("hero", block => block.Field("heading", "text"))
            .AddCollection("article", type => type.Field("headline", "text"))
            .Build();
        var failure = new InvalidOperationException("Content type 'article' has no field 'headline' in block 'hero'; token s3cret-value");

        var text = JobErrorText.ForStorage(failure, """{"t":"article","f":"headline","b":"hero","s":"s3cret-value"}""", JobErrorText.NamesOf(schema));

        JobErrorText.NamesOf(schema).ShouldBe(["article", "headline", "hero", "heading"], ignoreOrder: true);
        text.ShouldBe("System.InvalidOperationException: Content type 'article' has no field 'headline' in block 'hero'; token ***");
    }

    [Fact]
    public async Task AFailureStoresTheNamesOfTheSchemaThatItsPayloadHolds()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("article", type => type.Field("headline", "text")).Build();
        var instance = _fixture.NewInstance(cms: cms => cms.Services.AddSingleton(schema));
        _fixture.Probe.Behavior = (payload, _) => throw new InvalidOperationException($"Content type '{payload.Key}' refused {payload.Secret}");
        var id = await _fixture.EnqueueAsync("article", secret: "s3cret-value");

        (await instance.GetRequiredService<JobExecutor>().RunNextAsync(Queue, Owner, TestContext.Current.CancellationToken)).ShouldBeTrue();

        _fixture.Job(id).LastError.ShouldBe("System.InvalidOperationException: Content type 'article' refused ***");
    }

    [Fact]
    public void AShortValueIsTakenOutOnlyAsAWholeWord()
    {
        var text = JobErrorText.ForStorage(new InvalidOperationException("stored 12 in 123 at 12:30"), """{"to":12}""");

        text.ShouldBe("System.InvalidOperationException: stored *** in 123 at ***:30");
    }

    [Fact]
    public async Task ABadPayloadDoesNotStoreADictionaryKey()
    {
        var instance = _fixture.NewInstance(cms: cms => cms.Jobs.AddJob<DictionaryJob, Dictionary<string, int>>("dictionary"));
        var id = _fixture.AddRow("dictionary", """{"sk_live_abcdef":"not a number"}""", maxAttempts: 1);

        (await instance.GetRequiredService<JobExecutor>().RunNextAsync(Queue, Owner, TestContext.Current.CancellationToken)).ShouldBeTrue();

        var error = _fixture.Job(id).LastError.ShouldNotBeNull();
        error.ShouldStartWith("FCMS2502");
        error.ShouldNotContain("sk_live_abcdef");
    }

    [Fact]
    public async Task AnActorMustHaveAnId()
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 }, actor: new JobActor(PrincipalKind.AdminUser, Guid.Empty, "no one"));

        await RunNextAsync();

        _fixture.Probe.Runs.ShouldBeEmpty();
        _fixture.Job(id).LastError.ShouldNotBeNull().ShouldStartWith("FCMS2507");
    }

    [Fact]
    public async Task AnUnboundedErrorIsCapped()
    {
        _fixture.Probe.Behavior = (_, _) => throw new InvalidOperationException(new string('x', 100_000));
        var id = await _fixture.EnqueueAsync();

        await RunNextAsync();

        var error = _fixture.Job(id).LastError.ShouldNotBeNull();
        error.Length.ShouldBe(Job.LastErrorMaxLength);
        error.ShouldEndWith("...");
        _fixture.Attempts(id).ShouldHaveSingleItem().Error!.Length.ShouldBe(Job.LastErrorMaxLength);
    }

    [Fact]
    public void ACapDoesNotSplitACharacter()
    {
        var text = new string('x', Job.LastErrorMaxLength - 4) + "\U0001F600" + new string('x', 10);

        var capped = JobErrorText.Cap(text);

        capped.ShouldEndWith("x...");
        capped.Length.ShouldBeLessThanOrEqualTo(Job.LastErrorMaxLength);
    }

    [Fact]
    public async Task ASinkThatThrowsChangesNothingAboutTheJob()
    {
        _fixture.Events.Throws = true;
        AlwaysFail();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        (await RunNextAsync()).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);
        _fixture.Events.DeadLettered.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AJobThatLosesItsLeaseIsCancelledAndLeavesTheRowAlone()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            finally
            {
                cancelled.SetResult();
            }
        };

        var id = await _fixture.EnqueueAsync();
        var running = Task.Run(() => RunNextAsync(), TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        // Another worker has taken the job, as it would after a stall longer than the lease.
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>()
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(job => job.SetProperty(row => row.LeaseOwner, OtherOwner), TestContext.Current.CancellationToken);
        }

        await Eventually.CompletesAsync(running, () => _fixture.Clock.Advance(new JobRunnerOptions().HeartbeatInterval));

        cancelled.Task.IsCompleted.ShouldBeTrue();
        var row = _fixture.Job(id);
        row.Status.ShouldBe(JobStatus.Running);
        row.LeaseOwner.ShouldBe(OtherOwner);
        row.LastError.ShouldBeNull();
        _fixture.Events.Failed.ShouldBeEmpty();
    }

    private sealed class DictionaryJob : IJob<Dictionary<string, int>>
    {
        public Task ExecuteAsync(Dictionary<string, int> payload, JobContext context) => Task.CompletedTask;
    }

    private sealed class UnregisteredJob : IJob<ProbePayload>
    {
        public Task ExecuteAsync(ProbePayload payload, JobContext context) => Task.CompletedTask;
    }

    private sealed class ScalarJob<T>(ConcurrentQueue<object> seen) : IJob<T>
    {
        public Task ExecuteAsync(T payload, JobContext context)
        {
            seen.Enqueue(payload!);
            return Task.CompletedTask;
        }
    }
}
