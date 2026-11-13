using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Management;

public sealed class SmtpSettings
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 25;

    [SettingsSecret]
    public string? Password { get; set; }
}

// The records that took the place of anonymous objects and JSON trees in #2819 write the same text as those did: each old
// body is built here as the route built it, and both are written as AdminJson.Ok writes them.
public sealed class AdminBodiesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ListsAndPagesAreItemsWithTheirPaging()
    {
        SiteLocaleInfo[] locales = [new("en", 0, null, true), new("de", 1, ["en"], false)];

        Text(new ItemList<SiteLocaleInfo>(locales)).ShouldBe(Text(new { items = locales.AsEnumerable() }));
        Text(new ItemPage<SiteLocaleInfo>(locales, 12, 2, 2)).ShouldBe(Text(new { items = locales.AsEnumerable(), total = 12L, page = 2, pageSize = 2 }));
    }

    [Fact]
    public void ARoleKeepsItsGrantsAndTheirConstraints()
    {
        RoleInfo role = new(Guid.NewGuid(), Guid.NewGuid(), "Editor", null, BuiltIn: false, RequireTwoFactor: true,
        [
            new("content.*.read", PermissionConstraints.None),
            new("content.page.update", new PermissionConstraints { Sites = [new SiteId(Guid.NewGuid())], Locales = ["en"], RowRule = "own" }),
            new("activity.read", new PermissionConstraints { Fields = ["title"] }),
        ]);

        Text(RoleResponse.From(role)).ShouldBe(Text(OldRole(role)));
    }

    [Fact]
    public void ACreatedKeyIsTheKeyAndItsSecret()
    {
        CreatedApiKey created = new(Key(), "fcms_dk_secret");

        // The old body's dates were strings by the time it was written, so the + of their offset was escaped as +, as
        // no other key response does; read back, the two are the same.
        Text(CreatedApiKeyResponse.From(created)).ShouldNotBe(Text(OldCreatedKey(created)));
        JsonNode.Parse(Text(CreatedApiKeyResponse.From(created)))!.ToJsonString().ShouldBe(JsonNode.Parse(Text(OldCreatedKey(created)))!.ToJsonString());
        CreatedApiKeyResponse.From(created).ToString().ShouldNotContain("fcms_dk_secret");
    }

    [Fact]
    public void ASettingHasItsValueAndItsSchema()
    {
        SettingsDescriptor descriptor = new("smtp", typeof(SmtpSettings));
        RedactedSetting setting = new("""{"host":"mail.example.com","port":587}""", SettingsSource.Site, 3, ["password"], ["password"]);
        var site = Guid.NewGuid();

        Text(SettingResponse.From(descriptor, site, setting)).ShouldBe(Text(OldSetting(descriptor, site, setting)));
        Text(SettingResponse.From(descriptor, null, setting with { Source = SettingsSource.Code, SecretsSet = [], SecretsInherited = [] }))
            .ShouldBe(Text(OldSetting(descriptor, null, setting with { Source = SettingsSource.Code, SecretsSet = [], SecretsInherited = [] })));
    }

    [Fact]
    public void MeIsAUserWithItsRolesOrAnotherCallerByIdKindAndName()
    {
        UserInfo user = new(Guid.NewGuid(), "ada", null, false, At, null, true, false, null, [new UserRoleInfo(Guid.NewGuid(), "Editor", null)]);
        CurrentPrincipal me = new(user.Id, PrincipalKind.AdminUser, "Ada", user);
        CurrentPrincipal key = new(Guid.NewGuid(), PrincipalKind.ApiKey, "Reader", null);

        Text(MeResponse.From(me, user)).ShouldBe(Text(new { id = me.Id, kind = me.Kind, name = me.Name, user.UserName, user.Email, user.EmailConfirmed, user.MustChangePassword, user.TwoFactorEnabled, user.Roles }));
        Text(new PrincipalResponse(key.Id, key.Kind, key.Name)).ShouldBe(Text(new { id = key.Id, kind = key.Kind, name = key.Name }));
    }

    [Fact]
    public void AJobHasItsAttemptsAsHistory()
    {
        JobInfo job = new(Guid.NewGuid(), "test.failing", "default", null, "dead_lettered", 1, 1, 0, At, "boom", At, null, Origin.Job, At);
        JobDetail detail = new(job, [new JobAttemptInfo(1, At, At, "failed", "boom")]);

        Text(JobResponse.From(detail)).ShouldBe(Text(OldJob(detail)));
        Keys(JobResponse.From(detail)).ShouldBe([.. Keys(job), "history"]);
    }

    [Fact]
    public void RecoveryCodesAreAList()
    {
        string[] codes = ["a1b2-c3d4", "e5f6-g7h8"];

        Text(new RecoveryCodesResponse(codes)).ShouldBe(Text(new { recoveryCodes = (IReadOnlyList<string>)codes }));
    }

    // The old report was a JSON tree, written with the host's JsonOptions, whose names and values it does not change.
    [Fact]
    public void AHealthReportHasEveryCheckInOrderOfName()
    {
        HealthReport report = new(
            new Dictionary<string, HealthReportEntry>
            {
                ["process"] = new(HealthStatus.Healthy, null, TimeSpan.FromMilliseconds(0.25), null, null, ["live"]),
                ["database"] = new(HealthStatus.Unhealthy, "Server=db;Password=hunter2", TimeSpan.FromMilliseconds(12.5), new InvalidOperationException("Password=hunter2 refused"), null, ["ready", "db"]),
            },
            TimeSpan.FromMilliseconds(13.125));

        var text = Text(HealthReportResponse.From(report));

        text.ShouldBe(JsonSerializer.Serialize(OldHealth(report), JsonSerializerOptions.Web));
        text.ShouldNotContain("hunter2");
    }

    private static string Text(object value) => JsonSerializer.Serialize(value, AdminJson.Options);

    private static IEnumerable<string> Keys(object value) => JsonNode.Parse(Text(value))!.AsObject().Select(item => item.Key);

    private static ApiKeyInfo Key() =>
        new(Guid.NewGuid(), "Site reader", ApiKeyClass.Delivery, "fcms_dk_ab", Guid.NewGuid(), [Guid.NewGuid()], null, At, null, 100, null, At, Guid.NewGuid(), ["203.0.113.0/24"], At);

    private static JsonObject OldRole(RoleInfo role) => new()
    {
        ["id"] = role.Id,
        ["siteId"] = role.SiteId,
        ["name"] = role.Name,
        ["description"] = role.Description,
        ["builtIn"] = role.BuiltIn,
        ["requireTwoFactor"] = role.RequireTwoFactor,
        ["grants"] = new JsonArray([.. role.Grants.Select(grant => (JsonNode)new JsonObject
        {
            ["permission"] = grant.Permission,
            ["constraints"] = grant.Constraints.ToJson() is { } json ? JsonNode.Parse(json) : null,
        })]),
    };

    private static JsonObject OldCreatedKey(CreatedApiKey created)
    {
        var json = (JsonObject)JsonSerializer.SerializeToNode(created.Key, AdminJson.Options)!;
        json["secret"] = created.Secret;
        return json;
    }

    private static JsonObject OldSetting(SettingsDescriptor descriptor, Guid? site, RedactedSetting setting) => new()
    {
        ["area"] = descriptor.Area,
        ["siteId"] = site,
        ["source"] = JsonNamingPolicy.CamelCase.ConvertName(setting.Source.ToString()),
        ["version"] = setting.Version,
        ["value"] = JsonNode.Parse(setting.Json),
        ["secretsSet"] = new JsonArray([.. setting.SecretsSet.Select(key => (JsonNode)key)]),
        ["secretsInherited"] = new JsonArray([.. setting.SecretsInherited.Select(key => (JsonNode)key)]),
        ["schema"] = descriptor.CreateSchema(),
    };

    private static object OldJob(JobDetail detail) => new
    {
        detail.Job.Id,
        detail.Job.Type,
        detail.Job.Queue,
        detail.Job.SiteId,
        detail.Job.Status,
        detail.Job.Attempts,
        detail.Job.MaxAttempts,
        detail.Job.Priority,
        detail.Job.RunAt,
        detail.Job.LastError,
        detail.Job.CreatedAt,
        detail.Job.CreatedBy,
        detail.Job.Origin,
        detail.Job.CompletedAt,
        history = detail.Attempts,
    };

    private static JsonObject OldHealth(HealthReport report) => new()
    {
        ["status"] = report.Status.ToString(),
        ["totalDurationMs"] = report.TotalDuration.TotalMilliseconds,
        ["checks"] = new JsonArray([.. report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (JsonNode)new JsonObject
        {
            ["name"] = entry.Key,
            ["status"] = entry.Value.Status.ToString(),
            ["durationMs"] = entry.Value.Duration.TotalMilliseconds,
            ["tags"] = new JsonArray([.. entry.Value.Tags.Order(StringComparer.Ordinal).Select(tag => (JsonNode?)JsonValue.Create(tag))]),
            ["description"] = entry.Value.Description is { } description ? SecretScrubber.Of(description) : null,
            ["error"] = entry.Value.Exception is { } exception ? SecretScrubber.Of($"{exception.GetType().Name}: {exception.Message}") : null,
        })]),
    };
}
