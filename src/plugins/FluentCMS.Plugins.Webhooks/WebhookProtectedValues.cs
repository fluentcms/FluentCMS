using FluentCMS.Security;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Webhooks;

// The secrets and headers of the webhooks, for `fluentcms keyring reprotect`. A replaced secret whose overlap has ended
// signs nothing and the next write drops it, and plain headers, of before headers were protected, are under no key; neither
// is named. A write names the value it read, so a write in between wins.
internal sealed class WebhookProtectedValues(PluginDbContext<WebhooksPlugin> database, TimeProvider time) : IProtectedValueSource
{
    public const string SecretColumn = "secret";
    public const string PreviousSecretColumn = "previous_secret";
    public const string HeadersColumn = "headers";

    public async Task<IReadOnlyList<ProtectedValue>> ReadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await database.Set<Webhook>().AsNoTracking()
            .OrderBy(webhook => webhook.Id)
            .Select(webhook => new { webhook.Id, webhook.Secret, webhook.PreviousSecret, webhook.PreviousSecretExpiresAt, webhook.Headers })
            .ToListAsync(cancellationToken);

        List<ProtectedValue> values = [];
        foreach (var row in rows)
        {
            var id = row.Id.ToString("D");
            values.Add(new ProtectedValue(WebhookModel.WebhookTable, id, SecretColumn, WebhookSigning.SecretPurpose, row.Secret));
            if (row.PreviousSecret is { Length: > 0 } previous && row.PreviousSecretExpiresAt > now)
            {
                values.Add(new ProtectedValue(WebhookModel.WebhookTable, id, PreviousSecretColumn, WebhookSigning.SecretPurpose, previous));
            }

            if (!row.Headers.StartsWith('{'))
            {
                values.Add(new ProtectedValue(WebhookModel.WebhookTable, id, HeadersColumn, WebhookSigning.HeadersPurpose, row.Headers));
            }
        }

        return values;
    }

    public async Task<bool> WriteAsync(ProtectedValue value, string reprotected, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(value.Id);
        var stored = value.Stored;
        var webhooks = database.Set<Webhook>().Where(webhook => webhook.Id == id);
        var updated = value.Column switch
        {
            SecretColumn => await webhooks.Where(webhook => webhook.Secret == stored)
                .ExecuteUpdateAsync(set => set.SetProperty(webhook => webhook.Secret, reprotected), cancellationToken),
            PreviousSecretColumn => await webhooks.Where(webhook => webhook.PreviousSecret == stored)
                .ExecuteUpdateAsync(set => set.SetProperty(webhook => webhook.PreviousSecret, reprotected), cancellationToken),
            HeadersColumn => await webhooks.Where(webhook => webhook.Headers == stored)
                .ExecuteUpdateAsync(set => set.SetProperty(webhook => webhook.Headers, reprotected), cancellationToken),
            _ => throw new ArgumentException($"A webhook has no protected column '{value.Column}'.", nameof(value)),
        };
        return updated > 0;
    }
}
