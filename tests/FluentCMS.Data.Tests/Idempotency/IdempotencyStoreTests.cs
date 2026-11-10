using FluentCMS.Content;
using FluentCMS.Core.Idempotency;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Idempotency;

// The keys of #2933 on the run's database: the claim, the stored response, the refusals and the window.
public sealed class IdempotencyStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Principal Alice = new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "alice");

    private static readonly Principal Bob = new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "bob");

    private static readonly StoredResponse Created = new(201, new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8", ["Location"] = "/x/1" }, """{"id":"1"}""");

    private readonly ManualTimeProvider _clock = new(Start);

    private readonly ServiceDatabase _database;

    public IdempotencyStoreTests() =>
        _database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(_clock);
            cms.Idempotency.Window = TimeSpan.FromHours(24);
            cms.Idempotency.InProgressTimeout = TimeSpan.FromMinutes(5);
        });

    public void Dispose() => _database.Dispose();

    private IdempotencyStore Store => _database.Database.Services.GetRequiredService<IdempotencyStore>();

    private Task<IdempotencyStart> BeginAsync(string key = "key-1", string hash = "hash-a", Principal? principal = null, SiteId? site = null) =>
        Store.BeginAsync(new OperationContext(principal ?? Alice, Origin.Api, site ?? SiteA, "en", null, "c", _clock.GetUtcNow()), key, hash, Token);

    private List<IdempotencyRow> Rows()
    {
        using var scope = _database.Database.NewScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>().AsNoTracking()];
    }

    private static async Task<string> RefusedAsync(Func<Task> work) =>
        (await Should.ThrowAsync<ValidationException>(work)).Errors.ShouldHaveSingleItem().Code;

    [Fact]
    public async Task AFirstRequestClaimsTheKeyAndARepeatGetsItsStoredResponse()
    {
        var first = await BeginAsync();
        first.Stored.ShouldBeNull();
        var claim = first.Claim.ShouldNotBeNull();

        await Store.CompleteAsync(claim, Created);
        var repeat = await BeginAsync();

        repeat.Claim.ShouldBeNull();
        var stored = repeat.Stored.ShouldNotBeNull();
        stored.Status.ShouldBe(201);
        stored.Headers.ShouldBe(Created.Headers);
        stored.Body.ShouldBe(Created.Body);
        var row = Rows().ShouldHaveSingleItem();
        row.KeyHash.Length.ShouldBe(64);
        row.KeyHash.ShouldNotContain("key-1");
        row.ExpiresAt.ShouldBe(Start.AddHours(24));
    }

    [Fact]
    public async Task TheSameKeyWithAnotherRequestIsRefusedWhetherTheFirstRunsOrAnswered()
    {
        var claim = (await BeginAsync()).Claim!;
        (await RefusedAsync(() => BeginAsync(hash: "hash-b"))).ShouldBe(IdempotencyErrorCodes.KeyReused);

        await Store.CompleteAsync(claim, Created);
        (await RefusedAsync(() => BeginAsync(hash: "hash-b"))).ShouldBe(IdempotencyErrorCodes.KeyReused);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstRunsIsRefusedUntilItsLeaseRunsOutAndThenTakesItOver()
    {
        var first = (await BeginAsync()).Claim!;

        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = (await BeginAsync()).Claim.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);
        second.Owner.ShouldNotBe(first.Owner);

        // The first request answers late: its response is not kept, the one that took over is.
        await Store.CompleteAsync(first, Created with { Body = "late" });
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBeNull();
        await Store.CompleteAsync(second, Created);
        (await BeginAsync()).Stored!.Body.ShouldBe(Created.Body);
    }

    [Fact]
    public async Task OfManyRequestsThatSendOneKeyAtOnceOneRunsAndTheOthersAreRefused()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (await BeginAsync()).Claim is null ? "stored" : "claimed";
            }
            catch (ValidationException exception)
            {
                return exception.Errors.Single().Code;
            }
        }, Token)));

        attempts.Count(outcome => outcome == "claimed").ShouldBe(1);
        attempts.Where(outcome => outcome != "claimed").ShouldAllBe(outcome => outcome == IdempotencyErrorCodes.InProgress);
        Rows().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TwoPrincipalsAndTwoSitesNeverShareAKey()
    {
        await Store.CompleteAsync((await BeginAsync()).Claim!, Created);

        (await BeginAsync(principal: Bob, hash: "hash-b")).Claim.ShouldNotBeNull();
        (await BeginAsync(site: SiteB, hash: "hash-c")).Claim.ShouldNotBeNull();
        (await BeginAsync(principal: new Principal(PrincipalKind.ApiKey, Alice.Id, "key"), hash: "hash-d")).Claim.ShouldNotBeNull();
        Rows().Count.ShouldBe(4);
    }

    [Fact]
    public async Task AKeyIsComparedExactly()
    {
        await Store.CompleteAsync((await BeginAsync("Key-A")).Claim!, Created);

        (await BeginAsync("key-a", hash: "hash-b")).Claim.ShouldNotBeNull();
    }

    [Fact]
    public async Task AReleasedKeyRunsAgain()
    {
        await Store.ReleaseAsync((await BeginAsync()).Claim!);

        Rows().ShouldBeEmpty();
        (await BeginAsync(hash: "hash-b")).Claim.ShouldNotBeNull();
    }

    [Fact]
    public async Task AKeyPastItsWindowIsClaimedAfresh()
    {
        await Store.CompleteAsync((await BeginAsync()).Claim!, Created);

        _clock.Advance(TimeSpan.FromHours(24));
        var again = await BeginAsync(hash: "hash-b");

        again.Claim.ShouldNotBeNull();
        var row = Rows().ShouldHaveSingleItem();
        row.RequestHash.ShouldBe("hash-b");
        row.ResponseStatus.ShouldBeNull();
    }

    [Fact]
    public async Task TheRetentionJobRemovesTheKeysPastTheirWindowOnly()
    {
        await Store.CompleteAsync((await BeginAsync("old")).Claim!, Created);
        await BeginAsync("old-running");
        _clock.Advance(TimeSpan.FromHours(23));
        await BeginAsync("recent");
        _clock.Advance(TimeSpan.FromHours(1));

        using (var scope = _database.Database.NewScope())
        {
            var operation = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "retention", _clock.GetUtcNow());
            await scope.ServiceProvider.GetRequiredService<IdempotencyRetentionJob>().ExecuteAsync(
                new IdempotencyRetentionPayload(),
                new JobContext(Guid.CreateVersion7(), IdempotencyRetentionJob.Type, "default", 1, 3, operation, Token));
        }

        Rows().ShouldHaveSingleItem().ExpiresAt.ShouldBe(Start.AddHours(47));
    }

    [Fact]
    public async Task TheRetentionScheduleIsStoredAtStartup()
    {
        await _database.Database.Services.GetServices<IHostedService>().OfType<IdempotencyRetentionScheduleService>().Single().StartAsync(Token);

        using var scope = _database.Database.NewScope();
        var job = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Single(row => row.Name == IdempotencyRetentionJob.RecurringName);
        job.Type.ShouldBe(IdempotencyRetentionJob.Type);
        job.Cron.ShouldBe(IdempotencyRetentionJob.Schedule);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public void AKeyThatIsNotVisibleAsciiIsRefused(string key) =>
        Should.Throw<ValidationException>(() => IdempotencyStore.Key([key])).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdempotencyErrorCodes.InvalidKey);

    [Fact]
    public void AKeyIsOneValueOfAtMost255Characters()
    {
        IdempotencyStore.Key([new string('k', 255)]).Length.ShouldBe(255);
        IdempotencyStore.Key(["550e8400-e29b-41d4-a716-446655440000"]).ShouldBe("550e8400-e29b-41d4-a716-446655440000");
        Should.Throw<ValidationException>(() => IdempotencyStore.Key([new string('k', 256)]));
        Should.Throw<ValidationException>(() => IdempotencyStore.Key(["a", "b"])).Errors.ShouldHaveSingleItem().Path.ShouldBe("Idempotency-Key");
    }
}
