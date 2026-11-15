using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// GET and PUT /me/preferences over HTTP on SQLite (#3096): a user's admin preferences, kept for every session of the user
// and for nobody else, and refused to a key.
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

    [Fact]
    public async Task AKeyHasNoPreferences()
    {
        var key = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = fixture.Host.AsKey(key);

        var read = await fixture.GetAsync("me/preferences", client);
        read.Status.ShouldBe(HttpStatusCode.Forbidden, read.Body?.ToJsonString());
        read.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");
        (await fixture.PutAsync("me/preferences", """{ "culture": "en-US" }""", client)).Status.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = fixture.Host.CreateClient();
        (await fixture.GetAsync("me/preferences", anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
