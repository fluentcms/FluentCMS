using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Delivery;

// Expected failures are found before the unit of work begins, as in the other directories.
internal sealed class PreviewTokenDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IOptions<DeliveryOptions> options,
    TimeProvider clock) : IPreviewTokens
{
    private const string SubjectType = "preview_token";

    // Until multi-site (MST-01), every change set belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<IssuedPreviewToken>> IssueAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        if (RefuseAllButPeople() is { } refused)
        {
            return Result<IssuedPreviewToken>.Failure(refused.Errors);
        }

        var state = await StateAsync(changeSetId, cancellationToken);
        if (state is null)
        {
            return Result<IssuedPreviewToken>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"There is no change set {changeSetId} in this site.");
        }

        if (!PreviewTokenKeys.Previewable.Contains(state.Value))
        {
            return Result<IssuedPreviewToken>.Failure(
                DeliveryErrorCodes.ChangeSetNotPreviewable,
                $"Change set {changeSetId} is {state}, so there is nothing left to preview.");
        }

        var now = clock.GetUtcNow();
        var token = PreviewTokenKeys.Generate();
        var row = new PreviewTokenRow
        {
            Id = Guid.CreateVersion7(now),
            SiteId = SiteId,
            ChangeSetId = changeSetId,
            TokenHash = PreviewTokenKeys.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + options.Value.PreviewTokenLifetime,
        };

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        context.PreviewTokens.Add(row);
        await auditLog.AppendAsync(
            new NewAuditEvent("preview_token.issued") { SubjectType = SubjectType, SubjectId = row.Id, ChangeSetId = changeSetId },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<IssuedPreviewToken>.Success(new IssuedPreviewToken(row.Id, changeSetId, token, row.ExpiresAt));
    }

    public async Task<Result> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        if (RefuseAllButPeople() is { } refused)
        {
            return refused;
        }

        var row = await context.PreviewTokens.AsNoTracking().SingleOrDefaultAsync(token => token.Id == tokenId && token.SiteId == SiteId, cancellationToken);
        if (row is null)
        {
            return Result.Failure(DeliveryErrorCodes.PreviewTokenNotFound, $"There is no preview token {tokenId} in this site.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Only a token nobody revoked in the meantime is updated, so of two concurrent revokes the second changes nothing.
        var revoked = await context.PreviewTokens
            .Where(token => token.Id == tokenId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, clock.GetUtcNow()), cancellationToken);
        if (revoked == 0)
        {
            return Result.Failure(DeliveryErrorCodes.PreviewTokenNotFound, $"Preview token {tokenId} is already revoked.");
        }

        await auditLog.AppendAsync(
            new NewAuditEvent("preview_token.revoked") { SubjectType = SubjectType, SubjectId = tokenId, ChangeSetId = row.ChangeSetId },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PreviewToken>>> ListAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        if (await StateAsync(changeSetId, cancellationToken) is null)
        {
            return Result<IReadOnlyList<PreviewToken>>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"There is no change set {changeSetId} in this site.");
        }

        var rows = await context.PreviewTokens
            .AsNoTracking()
            .Where(token => token.ChangeSetId == changeSetId)
            .OrderByDescending(token => token.Id)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PreviewToken>>.Success(
            [.. rows.Select(row => new PreviewToken(row.Id, row.ChangeSetId, row.CreatedAt, row.ExpiresAt, row.RevokedAt))]);
    }

    // A token shows unapproved content to whoever holds it, so only a person hands one out (API-04).
    private Result? RefuseAllButPeople()
    {
        return currentInitiator.Current is UserInitiator
            ? null
            : Result.Failure(DeliveryErrorCodes.PreviewByPeopleOnly, "Only a person can issue or revoke a preview token; an agent cannot.");
    }

    private Task<ChangeSetState?> StateAsync(Guid changeSetId, CancellationToken cancellationToken)
    {
        return context.ChangeSets
            .AsNoTracking()
            .Where(set => set.Id == changeSetId && set.SiteId == SiteId)
            .Select(set => (ChangeSetState?)set.State)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
