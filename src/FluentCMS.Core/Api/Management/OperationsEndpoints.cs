using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The jobs, activity and me groups of spec 4.10. Jobs need jobs.manage for the instance; the activity log needs activity.read
// in the site of the request, and shows the rows of no site only to a reader of the instance; /me is the caller itself, and
// /me/two-factor its second factor.
internal static class OperationsEndpoints
{
    public static IServiceCollection AddManagementOperationsApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var jobs = management.MapGroup("/jobs").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        jobs.MapGet("", ListJobsAsync);
        jobs.MapGet("/dead-letter", DeadLettersAsync);
        jobs.MapGet("/{id}", GetJobAsync);
        jobs.MapPost("/{id}/retry", RetryAsync);
        jobs.MapPost("/{id}/cancel", CancelAsync);

        management.MapGroup("/activity").WithFluentCmsOperation(Origin.Api).RequireManagementScope().MapGet("", ActivityAsync);

        var me = management.MapGroup("/me").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        me.MapGet("", MeAsync);
        me.MapGet("/sessions", MySessionsAsync);
        me.MapDelete("/sessions/{sessionId}", RevokeMySessionAsync);
        me.MapGet("/two-factor", TwoFactorAsync);
        me.MapPost("/two-factor/enrol", EnrolAsync);
        me.MapPost("/two-factor/verify", VerifyAsync);
        me.MapPost("/two-factor/recovery-codes", RecoveryCodesAsync);
        me.MapPost("/two-factor/disable", DisableTwoFactorAsync);
    }

    private static async Task<IResult> ListJobsAsync(HttpContext httpContext, [FromServices] JobAdministration jobs)
    {
        var request = httpContext.Request;
        ManagementQuery.Only(request, "status", "type", "queue", "page", "pageSize");
        var (page, pageSize) = ManagementQuery.Page(request);
        var status = ManagementQuery.Text(request, "status");
        if (status is not null && !JobAdministration.Statuses.Contains(status, StringComparer.Ordinal))
        {
            throw new ValidationException([new ValidationError("status", QueryErrorCodes.InvalidValue, $"'{status}' is not one of {string.Join(", ", JobAdministration.Statuses)}.")]);
        }

        var filter = new JobFilter(status, ManagementQuery.Text(request, "type"), ManagementQuery.Text(request, "queue"));
        var found = await jobs.ListAsync(filter, page, pageSize, httpContext.RequestAborted);
        return AdminJson.Page(found.Items, found.Total, found.Page, found.PageSize);
    }

    private static async Task<IResult> DeadLettersAsync(HttpContext httpContext, [FromServices] JobAdministration jobs)
    {
        ManagementQuery.Only(httpContext.Request, "page", "pageSize");
        var (page, pageSize) = ManagementQuery.Page(httpContext.Request);
        return AdminJson.Items(await jobs.DeadLettersAsync(page, pageSize, httpContext.RequestAborted));
    }

    private static async Task<IResult> GetJobAsync(string id, HttpContext httpContext, [FromServices] JobAdministration jobs)
    {
        ManagementQuery.Only(httpContext.Request);
        return Job(await jobs.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> RetryAsync(string id, HttpContext httpContext, [FromServices] JobAdministration jobs)
    {
        ManagementQuery.Only(httpContext.Request);
        return Job(await jobs.RetryAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> CancelAsync(string id, HttpContext httpContext, [FromServices] JobAdministration jobs)
    {
        ManagementQuery.Only(httpContext.Request);
        return Job(await jobs.CancelAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> ActivityAsync(HttpContext httpContext, [FromServices] ActivityReader activity)
    {
        var request = httpContext.Request;
        ManagementQuery.Only(request, "actorId", "actorKind", "origin", "action", "targetType", "targetId", "from", "to", "page", "pageSize");
        var (page, pageSize) = ManagementQuery.Page(request);
        var filter = new ActivityFilter
        {
            ActorId = ManagementQuery.Id(request, "actorId"),
            ActorKind = ManagementQuery.Enum<PrincipalKind>(request, "actorKind"),
            Origin = ManagementQuery.Enum<Origin>(request, "origin"),
            Action = ManagementQuery.Text(request, "action"),
            TargetType = ManagementQuery.Text(request, "targetType"),
            TargetId = ManagementQuery.Text(request, "targetId"),
            From = ManagementQuery.Moment(request, "from"),
            To = ManagementQuery.Moment(request, "to"),
        };

        var found = await activity.QueryAsync(filter, new PageRequest { Number = page, Size = pageSize }, httpContext.RequestAborted);
        return AdminJson.Page(found.Items, found.Total ?? found.Items.Count, page, pageSize);
    }

    private static async Task<IResult> MeAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        ManagementQuery.Only(httpContext.Request);
        var me = await current.GetAsync(httpContext.RequestAborted);
        return me.User is { } user
            ? AdminJson.Ok(new { id = me.Id, kind = me.Kind, name = me.Name, user.UserName, user.Email, user.EmailConfirmed, user.MustChangePassword, user.TwoFactorEnabled, user.Roles })
            : AdminJson.Ok(new { id = me.Id, kind = me.Kind, name = me.Name });
    }

    private static async Task<IResult> MySessionsAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await current.SessionsAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> RevokeMySessionAsync(string sessionId, HttpContext httpContext, [FromServices] CurrentUser current)
    {
        ManagementQuery.Only(httpContext.Request);
        await current.RevokeSessionAsync(ManagementQuery.RouteId(sessionId, nameof(sessionId)), httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> TwoFactorAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await current.TwoFactorAsync(httpContext.RequestAborted));
    }

    // The key and its otpauth URI are shown here only; the authenticator app is named after the brand of the hosted pages.
    private static async Task<IResult> EnrolAsync(HttpContext httpContext, [FromServices] CurrentUser current, [FromServices] ISettings<BrandingSettings> branding)
    {
        ManagementQuery.Only(httpContext.Request);
        var issuer = (await branding.GetDefaultAsync(httpContext.RequestAborted)).Name;
        return AdminJson.Ok(await current.EnrolAuthenticatorAsync(issuer));
    }

    private static async Task<IResult> VerifyAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        var code = await CodeAsync(httpContext);
        return AdminJson.Ok(new { recoveryCodes = await current.VerifyAuthenticatorAsync(code) });
    }

    private static async Task<IResult> RecoveryCodesAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        var code = await CodeAsync(httpContext);
        return AdminJson.Ok(new { recoveryCodes = await current.RegenerateRecoveryCodesAsync(code) });
    }

    private static async Task<IResult> DisableTwoFactorAsync(HttpContext httpContext, [FromServices] CurrentUser current)
    {
        await current.DisableTwoFactorAsync(await CodeAsync(httpContext));
        return Results.NoContent();
    }

    private static async Task<string?> CodeAsync(HttpContext httpContext)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "code");
        var code = body.String("code", required: true);
        body.ThrowIfInvalid();
        return code;
    }

    private static IResult Job(JobDetail detail) =>
        AdminJson.Ok(new
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
        });
}
