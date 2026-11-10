using System.Globalization;
using FluentCMS.Activity;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Events;

namespace FluentCMS.Core.ActivityLog;

// The events of the catalogue that exist, and what each is in the log. An entry of content is described by its type, id,
// locale and version, never by a field: a reader of the log may not be allowed to read the entry. A new event of the
// catalogue is added here, and the test that lists every event record fails until it is.
internal static class ActivityEvents
{
    public static void Register(EventsBuilder events)
    {
        Entry<EntryDraftSavedEvent>(events, "content.draft_saved");
        Entry<EntryPublishedEvent>(events, "content.published");
        Entry<EntryUnpublishedEvent>(events, "content.unpublished");
        Entry<EntryDraftDiscardedEvent>(events, "content.draft_discarded");
        Entry<EntryVersionRestoredEvent>(events, "content.version_restored");
        Entry<EntryTrashedEvent>(events, "content.trashed");
        Entry<EntryRestoredEvent>(events, "content.restored");
        Entry<EntryPurgedEvent>(events, "content.purged");

        events.AddActivity<EntryScheduledEvent>("content.scheduled", message => ForEntry("content.scheduled", message, new Dictionary<string, string?>
        {
            ["version"] = Number(message.Version),
            ["at"] = message.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["schedule"] = message.Action,
        }));

        events.AddActivity<EntryScheduleCancelledEvent>("content.schedule_cancelled", message => ForEntry("content.schedule_cancelled", message, new Dictionary<string, string?>
        {
            ["version"] = Number(message.Version),
            ["schedule"] = message.Action,
        }));

        events.AddActivity<SettingsChangedEvent>("settings.changed", message => new ActivityEntry("settings.changed")
        {
            TargetType = "settings",
            TargetId = message.Area,
            TargetLabel = message.Area,
        });

        events.AddActivity<SiteCreatedEvent>("site.created", message => Site("site.created", message.SiteId, message.Slug));
        events.AddActivity<SiteUpdatedEvent>("site.updated", message => Site("site.updated", message.SiteId, message.Slug));

        events.AddActivity<PluginEnabledEvent>("plugin.enabled", message => Plugin("plugin.enabled", message.Plugin));
        events.AddActivity<PluginDisabledEvent>("plugin.disabled", message => Plugin("plugin.disabled", message.Plugin));

        events.AddActivity<UserCreatedEvent>("user.created", message => User("user.created", message.UserId, message.UserName, null));
        events.AddActivity<UserUpdatedEvent>("user.updated", message => User("user.updated", message.UserId, message.UserName, new Dictionary<string, string?>
        {
            ["changes"] = string.Join(',', message.Changes),
        }));
        events.AddActivity<UserDeactivatedEvent>("user.deactivated", message => User("user.deactivated", message.UserId, message.UserName, null));
        events.AddActivity<UserRoleChangedEvent>("user.role_changed", message => User("user.role_changed", message.UserId, message.UserName, new Dictionary<string, string?>
        {
            ["added"] = string.Join(',', message.Added.Select(id => id.ToString("D"))),
            ["removed"] = string.Join(',', message.Removed.Select(id => id.ToString("D"))),
        }));
        events.AddActivity<UserLoginLinkedEvent>("user.login_linked", message => User("user.login_linked", message.UserId, message.UserName, new Dictionary<string, string?>
        {
            ["provider"] = message.LoginProvider,
        }));
        events.AddActivity<UserLoginUnlinkedEvent>("user.login_unlinked", message => User("user.login_unlinked", message.UserId, message.UserName, new Dictionary<string, string?>
        {
            ["provider"] = message.LoginProvider,
        }));
        events.AddActivity<UserTwoFactorResetEvent>("user.two_factor_reset", message => User("user.two_factor_reset", message.UserId, message.UserName, null));

        events.AddActivity<LoginSucceededEvent>("user.login_succeeded", message => User("user.login_succeeded", message.UserId, message.UserName, new Dictionary<string, string?>
        {
            ["method"] = message.Method,
        }) with { Ip = message.Ip });

        // A sign-in that matched no user has no target.
        events.AddActivity<LoginFailedEvent>("user.login_failed", message => new ActivityEntry("user.login_failed")
        {
            TargetType = message.UserId is null ? null : "user",
            TargetId = message.UserId?.ToString("D"),
            TargetLabel = message.UserName,
            Details = new Dictionary<string, string?>
            {
                ["method"] = message.Method,
                ["reason"] = message.Reason,
            },
            Ip = message.Ip,
        });

        events.AddActivity<ApiKeyCreatedEvent>("api_key.created", message => Key("api_key.created", message.KeyId, message.Name, new Dictionary<string, string?>
        {
            ["class"] = message.KeyClass,
        }));
        events.AddActivity<ApiKeyRevokedEvent>("api_key.revoked", message => Key("api_key.revoked", message.KeyId, message.Name, null));
        events.AddActivity<ApiKeyExpiryChangedEvent>("api_key.expiry_changed", message => Key("api_key.expiry_changed", message.KeyId, message.Name, new Dictionary<string, string?>
        {
            ["expiresAt"] = message.ExpiresAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        }));
        events.AddActivity<ApiKeyRotatedEvent>("api_key.rotated", message => Key("api_key.rotated", message.KeyId, message.Name, new Dictionary<string, string?>
        {
            ["overlapEndsAt"] = message.PreviousKeyExpiresAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        }));
        events.AddActivity<ApiKeyAllowedAddressesChangedEvent>("api_key.allowed_addresses_changed", message => Key("api_key.allowed_addresses_changed", message.KeyId, message.Name, new Dictionary<string, string?>
        {
            ["allowedAddresses"] = message.AllowedAddresses is { } addresses ? string.Join(' ', addresses) : null,
        }));
        events.AddActivity<ApiKeyAddressRefusedEvent>("api_key.address_refused", message => Key("api_key.address_refused", message.KeyId, message.Name, new Dictionary<string, string?>
        {
            ["code"] = ApiKeyErrorCodes.AddressRefused,
        }) with
        {
            Ip = message.Ip,
        });

        events.AddActivity<McpClientRegisteredEvent>("mcp_client.registered", message => McpClient("mcp_client.registered", message.Id, message.ClientId, message.Name, new Dictionary<string, string?>
        {
            ["status"] = message.Status,
            ["role"] = message.RoleId?.ToString("D"),
        }) with
        {
            Ip = message.Ip,
        });
        events.AddActivity<McpClientApprovedEvent>("mcp_client.approved", message => McpClient("mcp_client.approved", message.Id, message.ClientId, message.Name, new Dictionary<string, string?>
        {
            ["role"] = message.RoleId.ToString("D"),
            ["roleName"] = message.RoleName,
        }));
        events.AddActivity<McpClientRejectedEvent>("mcp_client.rejected", message => McpClient("mcp_client.rejected", message.Id, message.ClientId, message.Name, []));
        events.AddActivity<McpClientRevokedEvent>("mcp_client.revoked", message => McpClient("mcp_client.revoked", message.Id, message.ClientId, message.Name, []));

        events.AddActivity<CacheInvalidatedEvent>("cache.invalidated", message => new ActivityEntry("cache.invalidated")
        {
            Details = new Dictionary<string, string?>
            {
                ["tags"] = string.Join(',', message.Tags),
                ["count"] = Number(message.Tags.Count),
            },
        });

        events.AddActivity<JobFailedEvent>("job.failed", message => new ActivityEntry("job.failed")
        {
            TargetType = "job",
            TargetId = message.JobId.ToString("D"),
            TargetLabel = message.Type,
            Details = new Dictionary<string, string?>
            {
                ["queue"] = message.Queue,
                ["attempt"] = Number(message.Attempt),
                ["maxAttempts"] = Number(message.MaxAttempts),
                ["error"] = message.Error,
            },
        });

        events.AddActivity<JobDeadLetteredEvent>("job.dead_lettered", message => new ActivityEntry("job.dead_lettered")
        {
            TargetType = "job",
            TargetId = message.JobId.ToString("D"),
            TargetLabel = message.Type,
            Details = new Dictionary<string, string?>
            {
                ["queue"] = message.Queue,
                ["attempts"] = Number(message.Attempts),
                ["error"] = message.Error,
            },
        });
    }

    private static void Entry<TEvent>(EventsBuilder events, string action)
        where TEvent : EntryEvent =>
        events.AddActivity<TEvent>(action, message => ForEntry(action, message, new Dictionary<string, string?> { ["version"] = Number(message.Version) }));

    private static ActivityEntry ForEntry(string action, EntryEvent message, Dictionary<string, string?> details) =>
        new(action)
        {
            TargetType = message.Type,
            TargetId = message.EntryId.ToString("D"),
            Locale = message.Locale,
            Details = details,
        };

    private static ActivityEntry Site(string action, Guid siteId, string slug) =>
        new(action)
        {
            TargetType = "site",
            TargetId = siteId.ToString("D"),
            TargetLabel = slug,
        };

    private static ActivityEntry Plugin(string action, string plugin) =>
        new(action)
        {
            TargetType = "plugin",
            TargetId = plugin,
            TargetLabel = plugin,
        };

    private static ActivityEntry McpClient(string action, Guid id, string clientId, string name, Dictionary<string, string?> details)
    {
        details["clientId"] = clientId;
        return new(action)
        {
            TargetType = "mcp_client",
            TargetId = id.ToString("D"),
            TargetLabel = name,
            Details = details,
        };
    }

    private static ActivityEntry User(string action, Guid userId, string userName, Dictionary<string, string?>? details) =>
        new(action)
        {
            TargetType = "user",
            TargetId = userId.ToString("D"),
            TargetLabel = userName,
            Details = details,
        };

    private static ActivityEntry Key(string action, Guid keyId, string name, Dictionary<string, string?>? details) =>
        new(action)
        {
            TargetType = "api_key",
            TargetId = keyId.ToString("D"),
            TargetLabel = name,
            Details = details,
        };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
