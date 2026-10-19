using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Security;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    IJobQueue jobs,
    IOptions<WebhookOptions> options,
    TimeProvider clock) : IWebhookDirectory
{
    private const string SubjectType = "webhook_subscription";

    private static readonly Result<UserInitiator> PeopleOnly = Result<UserInitiator>.Failure(
        WebhookErrorCodes.PeopleOnly,
        "Only a signed-in person can list, add, test or disable webhooks, or read their deliveries; an agent or an engine cannot.");

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
            return Result<WebhookSubscription>.Failure([NotFound(id)]);
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

    public async Task<Result<IReadOnlyList<WebhookDelivery>>> DeliveriesAsync(Guid id, int limit = 10, CancellationToken cancellationToken = default)
    {
        var person = Person();
        if (!person.IsSuccess)
        {
            return Result<IReadOnlyList<WebhookDelivery>>.Failure(person.Errors);
        }

        if (!await context.WebhookSubscriptions.AnyAsync(row => row.Id == id, cancellationToken))
        {
            return Result<IReadOnlyList<WebhookDelivery>>.Failure([NotFound(id)]);
        }

        var rows = await context.WebhookDeliveries
            .AsNoTracking()
            .Where(row => row.SubscriptionId == id)
            .OrderByDescending(row => row.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<WebhookDelivery>>.Success([.. rows.Select(row => new WebhookDelivery(
            row.Id,
            row.SubscriptionId,
            row.DeliveryId,
            row.Event,
            row.Attempt,
            row.AttemptedAt,
            row.StatusCode,
            row.Error,
            row.Duration))]);
    }

    public async Task<Result> SendTestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = Person();
        if (!person.IsSuccess)
        {
            return Result.Failure(person.Errors);
        }

        var row = await context.WebhookSubscriptions.AsNoTracking().SingleOrDefaultAsync(stored => stored.Id == id, cancellationToken);
        if (row is null)
        {
            return Result.Failure([NotFound(id)]);
        }

        if (row.DisabledAt is not null)
        {
            return Result.Failure(WebhookErrorCodes.Disabled, $"The webhook to {row.Url} is disabled, so nothing is sent to it; add it again to test a new one.");
        }

        var deliveryId = WebhookPayloads.NewDeliveryId();
        var body = WebhookPayloads.ForTest(deliveryId, clock.GetUtcNow(), id);

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        await jobs.EnqueueAsync(WebhookDeliveries.Job(id, deliveryId, WebhookEvents.Test, null, body, options.Value.MaxAttempts), cancellationToken);
        await auditLog.AppendAsync(new NewAuditEvent("webhook.test_requested") { SubjectType = SubjectType, SubjectId = id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result.Success();
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

    private static Error NotFound(Guid id)
    {
        return new Error(WebhookErrorCodes.NotFound, $"There is no webhook {id}; list the webhooks to find its id.");
    }
}
