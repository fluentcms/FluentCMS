using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Security;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Webhooks;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit. The
// audit events name the subscription and never its secret.
internal sealed class WebhookDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IOutboundHosts outbound,
    ISecretProtector protector,
    TimeProvider clock) : IWebhookDirectory
{
    private const string SubjectType = "webhook_subscription";

    private static readonly Result<UserInitiator> PeopleOnly = Result<UserInitiator>.Failure(
        WebhookErrorCodes.PeopleOnly,
        "Only a signed-in person can list, add or disable webhooks; an agent or an engine cannot.");

    public async Task<Result<IReadOnlyList<WebhookSubscription>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var person = Person();
        if (!person.IsSuccess)
        {
            return Result<IReadOnlyList<WebhookSubscription>>.Failure(person.Errors);
        }

        var rows = await context.WebhookSubscriptions
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<WebhookSubscription>>.Success([.. rows.Select(ToSubscription)]);
    }

    public async Task<Result<AddedWebhookSubscription>> AddAsync(NewWebhookSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var person = Person();
        if (!person.IsSuccess)
        {
            return Result<AddedWebhookSubscription>.Failure(person.Errors);
        }

        var added = WebhookSubscription.Add(subscription, outbound, person.Value, clock.GetUtcNow());
        if (!added.IsSuccess)
        {
            return Result<AddedWebhookSubscription>.Failure(added.Errors);
        }

        var secret = WebhookSecrets.Generate();
        var row = ToRow(added.Value, protector.Protect(WebhookSecrets.ProtectionPurpose, secret));

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        context.WebhookSubscriptions.Add(row);
        await auditLog.AppendAsync(new NewAuditEvent("webhook.added") { SubjectType = SubjectType, SubjectId = row.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<AddedWebhookSubscription>.Success(new AddedWebhookSubscription(added.Value, secret));
    }

    public async Task<Result<WebhookSubscription>> DisableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = Person();
        if (!person.IsSuccess)
        {
            return Result<WebhookSubscription>.Failure(person.Errors);
        }

        var row = await context.WebhookSubscriptions.AsNoTracking().SingleOrDefaultAsync(stored => stored.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<WebhookSubscription>.Failure(WebhookErrorCodes.NotFound, $"There is no webhook {id}; list the webhooks to find its id.");
        }

        var subscription = ToSubscription(row);
        var disabled = subscription.Disable(clock.GetUtcNow());
        if (!disabled.IsSuccess)
        {
            return Result<WebhookSubscription>.Failure(disabled.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Only a subscription nobody disabled in the meantime is updated, so of two concurrent disables the second changes nothing.
        var updated = await context.WebhookSubscriptions
            .Where(stored => stored.Id == id && stored.DisabledAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.DisabledAt, subscription.DisabledAt), cancellationToken);
        if (updated == 0)
        {
            return Result<WebhookSubscription>.Failure(
                WebhookErrorCodes.AlreadyDisabled,
                $"The webhook to {subscription.Url} was disabled by another request at the same time.");
        }

        await auditLog.AppendAsync(new NewAuditEvent("webhook.disabled") { SubjectType = SubjectType, SubjectId = id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<WebhookSubscription>.Success(subscription);
    }

    // A subscription sends content out of the product, which only a person decides: no agent, whatever its scope, and no engine.
    private Result<UserInitiator> Person()
    {
        return currentInitiator.Current is UserInitiator person ? Result<UserInitiator>.Success(person) : PeopleOnly;
    }

    private static WebhookSubscriptionRow ToRow(WebhookSubscription subscription, string protectedSecret)
    {
        return new WebhookSubscriptionRow
        {
            Id = subscription.Id,
            Url = subscription.Url.OriginalString,
            Events = [.. subscription.Events],
            ProtectedSecret = protectedSecret,
            AddedAt = subscription.AddedAt,
            AddedById = subscription.AddedBy.UserId,
            AddedByName = subscription.AddedBy.DisplayName,
            DisabledAt = subscription.DisabledAt,
        };
    }

    private static WebhookSubscription ToSubscription(WebhookSubscriptionRow row)
    {
        return WebhookSubscription.Restore(
            row.Id,
            new Uri(row.Url, UriKind.Absolute),
            row.Events,
            row.AddedAt,
            new UserInitiator(row.AddedById, row.AddedByName),
            row.DisabledAt);
    }
}
