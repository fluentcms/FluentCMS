using System.Data.Common;
using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// GET, PUT and PATCH /me/preferences over HTTP on SQLite (#3096, #3450): a user's admin preferences, kept for every session
// of the user and for nobody else, and refused to a key.
public sealed class MePreferencesTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    private const string Nothing = """{"culture":null,"timeZone":null,"dateFormat":null,"numberFormat":null}""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AUsersPreferencesAreKeptForEachOfItsSessions()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var laptop = await fixture.Host.AsUserAsync(user);
        using var phone = await fixture.Host.AsUserAsync(user);
        using var someoneElse = await fixture.ClientForAsync(BuiltInRoles.EditorName);

        (await fixture.GetAsync("me/preferences", laptop)).Json.ToJsonString().ShouldBe(Nothing);

        var saved = await fixture.PutAsync("me/preferences", """{ "culture": "de-de", "timeZone": "Europe/Berlin", "dateFormat": "en-GB", "numberFormat": "fr-FR" }""", laptop);

        saved.Status.ShouldBe(HttpStatusCode.OK, saved.Body?.ToJsonString());
        const string Chosen = """{"culture":"de-DE","timeZone":"Europe/Berlin","dateFormat":"en-GB","numberFormat":"fr-FR"}""";
        saved.Json.ToJsonString().ShouldBe(Chosen);
        (await fixture.GetAsync("me/preferences", phone)).Json.ToJsonString().ShouldBe(Chosen);
        (await fixture.GetAsync("me/preferences", someoneElse)).Json.ToJsonString().ShouldBe(Nothing);

        // A write replaces every value: one left out is no longer chosen.
        var changed = await fixture.PutAsync("me/preferences", """{ "timeZone": "America/New_York", "dateFormat": null }""", phone);
        changed.Json.ToJsonString().ShouldBe("""{"culture":null,"timeZone":"America/New_York","dateFormat":null,"numberFormat":null}""");
        (await fixture.GetAsync("me/preferences", laptop)).Json["timeZone"]!.GetValue<string>().ShouldBe("America/New_York");
    }

    // Two tabs of one user, each changing one preference, as the account page does (#3450).
    [Fact]
    public async Task APatchChangesTheMembersItNamesAndKeepsTheOthersAsStored()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var first = await fixture.Host.AsUserAsync(user);
        using var second = await fixture.Host.AsUserAsync(user);

        var created = await fixture.PatchAsync("me/preferences", """{ "timeZone": "Europe/Berlin" }""", first);
        created.Status.ShouldBe(HttpStatusCode.OK, created.Body?.ToJsonString());
        created.Json.ToJsonString().ShouldBe("""{"culture":null,"timeZone":"Europe/Berlin","dateFormat":null,"numberFormat":null}""");
        (await fixture.PutAsync("me/preferences", """{ "culture": "en-US", "timeZone": "Europe/Berlin", "dateFormat": "en-GB", "numberFormat": "fr-FR" }""", first)).Status.ShouldBe(HttpStatusCode.OK);

        // Each tab last read the four values above; neither sends the other's.
        var changes = await Task.WhenAll(
            fixture.PatchAsync("me/preferences", """{ "timeZone": "Asia/Tokyo" }""", first),
            fixture.PatchAsync("me/preferences", """{ "culture": "de-de" }""", second));

        changes.ShouldAllBe(change => change.Status == HttpStatusCode.OK);
        (await fixture.GetAsync("me/preferences", first)).Json.ToJsonString()
            .ShouldBe("""{"culture":"de-DE","timeZone":"Asia/Tokyo","dateFormat":"en-GB","numberFormat":"fr-FR"}""");

        var cleared = await fixture.PatchAsync("me/preferences", """{ "dateFormat": null }""", second);
        cleared.Json.ToJsonString().ShouldBe("""{"culture":"de-DE","timeZone":"Asia/Tokyo","dateFormat":null,"numberFormat":"fr-FR"}""");
        (await fixture.PatchAsync("me/preferences", "{}", second)).Json.ToJsonString().ShouldBe(cleared.Json.ToJsonString());

        var refused = await fixture.PatchAsync("me/preferences", """{ "culture": "not a culture", "numberFormat": null }""", first);
        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body?.ToJsonString());
        (await fixture.PatchAsync("me/preferences", """{ "theme": "dark" }""", first)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.GetAsync("me/preferences", first)).Json.ToJsonString().ShouldBe(cleared.Json.ToJsonString());
    }

    // Tab A reads, tab B changes another member and commits, then A writes its own: A is answered what is stored, B's change
    // included, with the tag of that, so a client that saves the answer whole cannot undo B (#3450).
    [Fact]
    public async Task APatchIsAnsweredWhatIsStoredWhenAnotherLandedBetweenItsReadAndItsWrite()
    {
        var hold = new Hold();
        await using var host = new FluentCmsTestHost()
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(new HoldCommands(hold), new HoldTransactions(hold))));
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var first = await host.AsUserAsync(user);
        using var second = await host.AsUserAsync(user);
        (await AdminFixture.SendAsync(first, HttpMethod.Put, "me/preferences", """{ "culture": "en-US", "timeZone": "Europe/Berlin" }""")).Status.ShouldBe(HttpStatusCode.OK);

        hold.Arm();
        var held = AdminFixture.SendAsync(first, HttpMethod.Patch, "me/preferences", """{ "culture": "de-DE" }""");
        await hold.Reached.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var other = await AdminFixture.SendAsync(second, HttpMethod.Patch, "me/preferences", """{ "timeZone": "Asia/Tokyo" }""");
        other.Status.ShouldBe(HttpStatusCode.OK, other.Body?.ToJsonString());
        hold.Release();
        var answer = await held;

        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body?.ToJsonString());
        var stored = await AdminFixture.SendAsync(first, HttpMethod.Get, "me/preferences");
        stored.Json.ToJsonString().ShouldBe("""{"culture":"de-DE","timeZone":"Asia/Tokyo","dateFormat":null,"numberFormat":null}""");
        answer.Json.ToJsonString().ShouldBe(stored.Json.ToJsonString());
        answer.ETag.ShouldBe(stored.ETag);
    }

    // A change that names nothing writes nothing: a user without preferences gets no row, and is answered the defaults.
    [Fact]
    public async Task AnEmptyPatchWritesNothing()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var client = await fixture.Host.AsUserAsync(user);

        var empty = await fixture.PatchAsync("me/preferences", "{}", client);

        empty.Status.ShouldBe(HttpStatusCode.OK, empty.Body?.ToJsonString());
        empty.Json.ToJsonString().ShouldBe(Nothing);
        empty.ETag.ShouldBe((await fixture.GetAsync("me/preferences", client)).ETag);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<UserPreferenceRow>().AnyAsync(row => row.UserId == user.Id, Token)).ShouldBeFalse();
    }

    // A write that names the ETag it read is refused once another tab changed the preferences, and that tab's change stays.
    [Fact]
    public async Task AWriteBasedOnAStaleETagIsRefusedAndNothingIsLost()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var first = await fixture.Host.AsUserAsync(user);
        using var second = await fixture.Host.AsUserAsync(user);

        var empty = await fixture.GetAsync("me/preferences", first);
        empty.ETag.ShouldNotBeNull();
        var saved = await AdminFixture.SendAsync(first, HttpMethod.Put, "me/preferences", """{ "culture": "en-US", "timeZone": "Europe/Berlin" }""", empty.ETag);
        saved.Status.ShouldBe(HttpStatusCode.OK, saved.Body?.ToJsonString());
        saved.ETag.ShouldNotBe(empty.ETag);

        var read = await fixture.GetAsync("me/preferences", first);
        read.ETag.ShouldBe(saved.ETag);
        (await fixture.GetAsync("me/preferences", second)).ETag.ShouldBe(read.ETag);

        var changed = await AdminFixture.SendAsync(second, HttpMethod.Patch, "me/preferences", """{ "timeZone": "Asia/Tokyo" }""", read.ETag);
        changed.Status.ShouldBe(HttpStatusCode.OK, changed.Body?.ToJsonString());
        changed.ETag.ShouldNotBe(read.ETag);

        foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Put })
        {
            var stale = await AdminFixture.SendAsync(first, method, "me/preferences", """{ "culture": "fr-FR" }""", read.ETag);
            stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed, stale.Body?.ToJsonString());
            stale.Json["code"]!.GetValue<string>().ShouldBe("FCMS3105");
        }

        var stored = await fixture.GetAsync("me/preferences", first);
        stored.Json.ToJsonString().ShouldBe("""{"culture":"en-US","timeZone":"Asia/Tokyo","dateFormat":null,"numberFormat":null}""");
        stored.ETag.ShouldBe(changed.ETag);

        var again = await AdminFixture.SendAsync(first, HttpMethod.Patch, "me/preferences", """{ "culture": "fr-FR" }""", $"W/{stored.ETag}");
        again.Status.ShouldBe(HttpStatusCode.OK, again.Body?.ToJsonString());
        again.Json["timeZone"]!.GetValue<string>().ShouldBe("Asia/Tokyo");
        (await AdminFixture.SendAsync(first, HttpMethod.Patch, "me/preferences", """{ "dateFormat": "de-DE" }""", "*")).Status.ShouldBe(HttpStatusCode.OK);

        var invalid = await AdminFixture.SendAsync(first, HttpMethod.Patch, "me/preferences", """{ "dateFormat": null }""", "unquoted");
        invalid.Status.ShouldBe(HttpStatusCode.BadRequest, invalid.Body?.ToJsonString());
        invalid.Json["code"]!.GetValue<string>().ShouldBe("FCMS3131");
        (await AdminFixture.SendAsync(first, HttpMethod.Patch, "me/preferences", "{}", $"{stored.ETag}, {again.ETag}")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.GetAsync("me/preferences", first)).Json["dateFormat"]!.GetValue<string>().ShouldBe("de-DE");
    }

    // The tag is of the values alone, so it says nothing of the user, and the same values give the same tag.
    [Fact]
    public async Task TheETagFollowsTheValuesAndNothingElse()
    {
        using var one = await fixture.ClientForAsync(BuiltInRoles.EditorName);
        using var other = await fixture.ClientForAsync(BuiltInRoles.EditorName);
        const string Values = """{ "culture": "en-GB", "timeZone": "Europe/London", "dateFormat": null, "numberFormat": null }""";

        var first = await fixture.PutAsync("me/preferences", Values, one);
        (await fixture.PutAsync("me/preferences", Values, other)).ETag.ShouldBe(first.ETag);
        (await fixture.PutAsync("me/preferences", Values, one)).ETag.ShouldBe(first.ETag);

        string?[] tags =
        [
            first.ETag,
            (await fixture.PatchAsync("me/preferences", """{ "culture": "en-US" }""", one)).ETag,
            (await fixture.PatchAsync("me/preferences", """{ "timeZone": "Europe/Paris" }""", one)).ETag,
            (await fixture.PatchAsync("me/preferences", """{ "dateFormat": "en-US" }""", one)).ETag,
            (await fixture.PatchAsync("me/preferences", """{ "numberFormat": "en-US" }""", one)).ETag,
            (await fixture.PatchAsync("me/preferences", """{ "numberFormat": null }""", one)).ETag,
        ];
        tags.Distinct().Count().ShouldBe(tags.Length - 1);
        tags[^1].ShouldBe(tags[3]);
    }

    [Fact]
    public async Task WhatIsNotACultureOrAnIanaTimeZoneIsRefusedAndNothingIsKept()
    {
        using var user = await fixture.ClientForAsync(BuiltInRoles.EditorName);

        var refused = await fixture.PutAsync("me/preferences", $$"""{ "culture": "not a culture", "timeZone": "Mars/Olympus", "dateFormat": "{{new string('a', 33)}}", "numberFormat": "" }""", user);

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body?.ToJsonString());
        refused.Json["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>()).ShouldBe(["culture", "dateFormat", "numberFormat", "timeZone"], ignoreOrder: true);
        (await fixture.PutAsync("me/preferences", """{ "timeZone": "W. Europe Standard Time" }""", user)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.PutAsync("me/preferences", """{ "culture": " en-US" }""", user)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.PutAsync("me/preferences", """{ "culture": 1 }""", user)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var unknown = await fixture.PutAsync("me/preferences", """{ "theme": "dark" }""", user);
        unknown.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, unknown.Body?.ToJsonString());
        unknown.Json["code"]!.GetValue<string>().ShouldBe("FCMS2108");
        (await fixture.GetAsync("me/preferences", user)).Json.ToJsonString().ShouldBe(Nothing);
    }

    // The platform finds a zone it has already found in any case; another case is refused before and after that.
    [Fact]
    public async Task ATimeZoneInAnotherCaseIsRefusedBeforeAndAfterItsExactIdIsTaken()
    {
        using var user = await fixture.ClientForAsync(BuiltInRoles.EditorName);

        var first = await fixture.PutAsync("me/preferences", """{ "timeZone": "pacific/CHATHAM" }""", user);
        first.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, first.Body?.ToJsonString());
        first.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("timeZone");

        (await fixture.PutAsync("me/preferences", """{ "timeZone": "Pacific/Chatham" }""", user)).Status.ShouldBe(HttpStatusCode.OK);

        var after = await fixture.PutAsync("me/preferences", """{ "timeZone": "pacific/CHATHAM" }""", user);
        after.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, after.Body?.ToJsonString());
        after.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("timeZone");
        (await fixture.GetAsync("me/preferences", user)).Json["timeZone"]!.GetValue<string>().ShouldBe("Pacific/Chatham");
    }

    [Fact]
    public async Task AKeyHasNoPreferences()
    {
        var key = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = fixture.Host.AsKey(key);

        var read = await fixture.GetAsync("me/preferences", client);
        read.Status.ShouldBe(HttpStatusCode.Forbidden, read.Body?.ToJsonString());
        read.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");
        (await fixture.PutAsync("me/preferences", """{ "culture": "en-US" }""", client)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PatchAsync("me/preferences", """{ "culture": "en-US" }""", client)).Status.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = fixture.Host.CreateClient();
        (await fixture.GetAsync("me/preferences", anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Holds the first write of the preferences after its read, armed, until released: at its transaction, or at its update
    // when it has none.
    private sealed class Hold
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _held;
        private int _armed;
        private int _done;

        public Task Reached => _reached.Task;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public void Release() => _released.TrySetResult();

        public void Read(DbContext? context)
        {
            if (Volatile.Read(ref _armed) == 1 && context is not null)
            {
                Interlocked.CompareExchange(ref _held, context, null);
            }
        }

        public async Task WaitAsync(DbContext? context)
        {
            if (context is not null && ReferenceEquals(context, Volatile.Read(ref _held)) && Interlocked.Exchange(ref _done, 1) == 0)
            {
                _reached.TrySetResult();
                await _released.Task;
            }
        }
    }

    private sealed class HoldCommands(Hold hold) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("\"cms_user_preference\"", StringComparison.Ordinal))
            {
                hold.Read(eventData.Context);
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("\"cms_user_preference\"", StringComparison.Ordinal))
            {
                await hold.WaitAsync(eventData.Context);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class HoldTransactions(Hold hold) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            await hold.WaitAsync(eventData.Context);
            return await base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
