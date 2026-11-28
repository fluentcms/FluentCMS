using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Core.Storage;

// Only the conditional row change and its outbox insert hold the write transaction, never preparation of the secrets.
internal sealed class SettingRows(SystemDbContext db, string area, Guid? site, OutboxWriter outbox)
{
    public IQueryable<SettingRow> For(Guid? scope)
    {
        var rows = db.Set<SettingRow>().Where(row => row.Key == area);
        return scope is { } one ? rows.Where(row => row.SiteId == one) : rows.Where(row => row.SiteId == null);
    }

    public static ValidationException SiteNotFound(SiteId site) =>
        SettingsDescriptor.Fail("site", SettingsErrorCodes.SiteNotFound, $"There is no site {site}.");

    public async Task EnsureSiteAsync(CancellationToken cancellationToken)
    {
        if (site is { } one && (one == Guid.Empty || !await db.Set<Site>().AnyAsync(item => item.Id == one, cancellationToken)))
        {
            throw SiteNotFound(new SiteId(one));
        }
    }

    public async Task<bool> TryStoreAsync(int? version, string json, Guid actor, DateTimeOffset now, IReadOnlyList<OutboxMessage> prepared, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (version is { } previous)
        {
            var updated = await For(site).Where(row => row.Version == previous).ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Json, json)
                    .SetProperty(row => row.Version, previous + 1)
                    .SetProperty(row => row.UpdatedAt, now)
                    .SetProperty(row => row.UpdatedBy, actor),
                cancellationToken);
            if (updated == 0)
            {
                return false;
            }
        }
        else
        {
            db.Add(new SettingRow { Id = Guid.CreateVersion7(), SiteId = site, Key = area, Json = json, Version = 1, UpdatedAt = now, UpdatedBy = actor });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Check for a competing insert outside the failed transaction, which may no longer accept a read.
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                await EnsureSiteAsync(cancellationToken);
                if (!await For(site).AnyAsync(cancellationToken))
                {
                    throw;
                }

                return false;
            }
        }

        await StageAndCommitAsync(transaction, prepared, cancellationToken);
        return true;
    }

    public async Task<bool> TryRemoveAsync(int? version, IReadOnlyList<OutboxMessage> prepared, CancellationToken cancellationToken)
    {
        var rows = For(site);
        if (version is { } previous)
        {
            rows = rows.Where(row => row.Version == previous);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await rows.ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return false;
        }

        await StageAndCommitAsync(transaction, prepared, cancellationToken);
        return true;
    }

    private async Task StageAndCommitAsync(IDbContextTransaction transaction, IReadOnlyList<OutboxMessage> prepared, CancellationToken cancellationToken)
    {
        await outbox.StagePreparedAsync(db, prepared, cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
    }
}
