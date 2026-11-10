using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluentCMS.Core.Api.Management;

// The bodies of the groups beside content that no service record has the shape of. Responses are written with
// AdminJson.Options; requests are read key by key, so a request record only describes its body in the OpenAPI document,
// where a key it does not name is refused as the route refuses it.
internal sealed record ItemList<T>(IEnumerable<T> Items);

internal sealed record ItemPage<T>(IEnumerable<T> Items, long Total, int Page, int PageSize);

internal sealed record RoleResponse(Guid Id, Guid? SiteId, string Name, string? Description, bool BuiltIn, bool RequireTwoFactor, IReadOnlyList<Grant> Grants)
{
    public static RoleResponse From(RoleInfo role) =>
        new(role.Id, role.SiteId, role.Name, role.Description, role.BuiltIn, role.RequireTwoFactor, [.. role.Grants.Select(Grant.From)]);
}

// A grant without constraints has null constraints.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record Grant
{
    public required string Permission { get; init; }

    public GrantConstraints? Constraints { get; init; }

    public static Grant From(PermissionGrant grant) => new() { Permission = grant.Permission, Constraints = GrantConstraints.From(grant.Constraints) };
}

// As stored: a member that does not narrow the grant is left out.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GrantConstraints
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? Sites { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Locales { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Fields { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RowRule { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Resources { get; init; }

    public static GrantConstraints? From(PermissionConstraints constraints) => constraints.IsEmpty
        ? null
        : new()
        {
            Sites = constraints.Sites?.Select(site => site.Value).ToList(),
            Locales = constraints.Locales,
            Fields = constraints.Fields,
            RowRule = constraints.RowRule,
            Resources = constraints.Resources,
        };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RoleCreateRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    [Description("The site of a role of one site; left out, the role is for the instance.")]
    public Guid? SiteId { get; init; }

    public bool? RequireTwoFactor { get; init; }

    public required IReadOnlyList<Grant> Grants { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RoleUpdateRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    [Description("Left out, the role keeps the value it has.")]
    public bool? RequireTwoFactor { get; init; }

    public required IReadOnlyList<Grant> Grants { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvitationRequest
{
    public required string Email { get; init; }

    public IReadOnlyList<Guid>? RoleIds { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ApiKeyCreateRequest
{
    public required string Name { get; init; }

    public required ApiKeyClass Class { get; init; }

    public required Guid RoleId { get; init; }

    [Description("The sites the key is bound to; left out, it is bound to no site and reaches every one. An empty list is refused.")]
    public IReadOnlyList<Guid>? SiteIds { get; init; }

    [Description("The content types the key reads; left out, every type. An empty list is refused.")]
    public IReadOnlyList<string>? TypeNames { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public int? RateLimit { get; init; }

    [Description("The IP addresses and CIDR ranges the key is accepted from, such as 203.0.113.0/24; left out, any address. An empty list is refused.")]
    public IReadOnlyList<string>? AllowedAddresses { get; init; }
}

// The key as ApiKeyInfo has it, and its secret, which no other response has. The secret stays out of ToString.
internal sealed record CreatedApiKeyResponse(
    Guid Id,
    string Name,
    ApiKeyClass Class,
    string KeyPrefix,
    Guid RoleId,
    IReadOnlyList<Guid>? SiteIds,
    IReadOnlyList<string>? TypeNames,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    int? RateLimit,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    IReadOnlyList<string>? AllowedAddresses,
    DateTimeOffset? PreviousKeyExpiresAt,
    string Secret)
{
    public static CreatedApiKeyResponse From(CreatedApiKey created)
    {
        ArgumentNullException.ThrowIfNull(created);

        var key = created.Key;
        return new(key.Id, key.Name, key.Class, key.KeyPrefix, key.RoleId, key.SiteIds, key.TypeNames, key.ExpiresAt, key.LastUsedAt, key.RateLimit, key.RevokedAt, key.CreatedAt, key.CreatedBy, key.AllowedAddresses, key.PreviousKeyExpiresAt, created.Secret);
    }

    public override string ToString() => $"{nameof(CreatedApiKeyResponse)} {{ Id = {Id} }}";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ApiKeyExpireRequest
{
    [Description("When the key expires; left out, now.")]
    public DateTimeOffset? At { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ApiKeyRotateRequest
{
    [Description("How long the replaced secret is still accepted, in seconds, from 0 to the configured RotationOverlap; left out, RotationOverlap.")]
    public int? OverlapSeconds { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ApiKeyAllowedAddressesRequest
{
    [Description("The IP addresses and CIDR ranges the key is accepted from; null for any address. An empty list is refused.")]
    public required IReadOnlyList<string>? AllowedAddresses { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SiteLocaleCreateRequest
{
    public required string Code { get; init; }

    public int? Position { get; init; }

    [Description("Left out, the locale is enabled.")]
    public bool? Enabled { get; init; }

    [Description("Other locales of the site, in order; null or left out is the implicit chain.")]
    public IReadOnlyList<string>? Fallbacks { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SiteLocaleUpdateRequest
{
    public int? Position { get; init; }

    public bool? Enabled { get; init; }

    [Description("Other locales of the site, in order; null goes back to the implicit chain, and leaving it out keeps the chain the locale has.")]
    public IReadOnlyList<string>? Fallbacks { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PluginSiteRequest
{
    [Description("Whether the plugin is enabled for the site. A plugin is enabled for every site until it is disabled for one.")]
    public required bool Enabled { get; init; }
}

internal sealed record SettingResponse(
    string Area,
    Guid? SiteId,
    SettingsSource Source,
    int Version,
    [property: Description("The settings of the area, as the JSON schema under schema describes them. A secret key is left out; secretsSet names those that hold a value.")]
    JsonObject Value,
    IReadOnlyList<string> SecretsSet,
    [property: Description("The JSON schema of the area's settings, which value and a write follow.")]
    JsonObject Schema)
{
    // A redacted document and a settings class's schema are both objects.
    public static SettingResponse From(SettingsDescriptor descriptor, Guid? site, RedactedSetting setting) =>
        new(descriptor.Area, site, setting.Source, setting.Version, JsonNode.Parse(setting.Json)!.AsObject(), setting.SecretsSet, descriptor.CreateSchema().AsObject());
}

// A job as JobInfo has it, with its attempts as history and never its payload.
internal sealed record JobResponse(
    Guid Id,
    string Type,
    string Queue,
    Guid? SiteId,
    string Status,
    int Attempts,
    int MaxAttempts,
    int Priority,
    DateTimeOffset RunAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    Origin Origin,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<JobAttemptInfo> History)
{
    public static JobResponse From(JobDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var job = detail.Job;
        return new(job.Id, job.Type, job.Queue, job.SiteId, job.Status, job.Attempts, job.MaxAttempts, job.Priority, job.RunAt, job.LastError, job.CreatedAt, job.CreatedBy, job.Origin, job.CompletedAt, detail.Attempts);
    }
}

// A user as /me shows it. A key or a service account answers PrincipalResponse, without the members of a user.
[Description("The caller. A key or a service account has id, kind and name only.")]
internal sealed record MeResponse
{
    public required Guid Id { get; init; }

    public required PrincipalKind Kind { get; init; }

    public required string Name { get; init; }

    public string UserName { get; init; } = "";

    public string? Email { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool MustChangePassword { get; init; }

    public bool TwoFactorEnabled { get; init; }

    public IReadOnlyList<UserRoleInfo> Roles { get; init; } = [];

    public static MeResponse From(CurrentPrincipal me, UserInfo user) => new()
    {
        Id = me.Id,
        Kind = me.Kind,
        Name = me.Name,
        UserName = user.UserName,
        Email = user.Email,
        EmailConfirmed = user.EmailConfirmed,
        MustChangePassword = user.MustChangePassword,
        TwoFactorEnabled = user.TwoFactorEnabled,
        Roles = user.Roles,
    };
}

internal sealed record PrincipalResponse(Guid Id, PrincipalKind Kind, string Name);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TwoFactorCodeRequest
{
    public required string Code { get; init; }
}

internal sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

// The report of /system/health, every check in order of name. A status is the name of a HealthStatus as it is, and a
// description or exception may quote a connection string, so every text goes through the scrubber.
internal sealed record HealthReportResponse(
    [property: Description("Healthy, Degraded or Unhealthy: the worst status of the checks.")]
    string Status,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckResponse> Checks)
{
    public static HealthReportResponse From(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            [.. report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => HealthCheckResponse.From(entry.Key, entry.Value))]);
    }
}

internal sealed record HealthCheckResponse(
    string Name,
    [property: Description("Healthy, Degraded or Unhealthy.")]
    string Status,
    double DurationMs,
    IReadOnlyList<string> Tags,
    string? Description,
    [property: Description("The type and message of the exception the check threw.")]
    string? Error)
{
    public static HealthCheckResponse From(string name, HealthReportEntry entry) => new(
        name,
        entry.Status.ToString(),
        entry.Duration.TotalMilliseconds,
        [.. entry.Tags.Order(StringComparer.Ordinal)],
        entry.Description is { } description ? SecretScrubber.Of(description) : null,
        entry.Exception is { } exception ? SecretScrubber.Of($"{exception.GetType().Name}: {exception.Message}") : null);
}
