using FluentCMS.Admin;
using FluentCMS.Admin.Audit;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Host.Audit;

// The download behind the audit page's export form: a page cannot answer with a file. It maps the form and calls the exporter,
// nothing more. Form binding makes the anti-forgery check apply, as it does to every page form (SEC-07).
internal static class AuditExportEndpoint
{
    public static void MapAuditExport(this IEndpointRouteBuilder endpoints)
    {
        // A forged token gets a bare 400. Re-executing the not-found page would read the rejected form again and fail with a 500.
        endpoints.MapPost(AdminRoutes.AuditExport, ExportAsync)
            .RequireAuthorization()
            .WithMetadata(new SkipStatusCodePagesAttribute());

        // Someone whose session ran out signs in again and is sent back here with a GET; the form is on the page.
        endpoints.MapGet(AdminRoutes.AuditExport, () => Results.LocalRedirect(AdminRoutes.Audit)).RequireAuthorization();
    }

    private static async Task<IResult> ExportAsync(HttpContext context, [FromForm] AuditExportForm form, IAuditExporter exporter)
    {
        var request = form.ToRequest();
        if (!request.IsSuccess)
        {
            return BackToForm(form, request.Errors);
        }

        var (query, format) = request.Value;
        var response = context.Response;
        response.ContentType = format == AuditExportFormat.Json ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = FileName(query, format),
        }.ToString();

        var exported = await exporter.ExportAsync(query, format, response.Body, context.RequestAborted);
        if (!exported.IsSuccess)
        {
            // Nothing has been written, so the response can still turn into the redirect.
            response.Clear();
            return BackToForm(form, exported.Errors);
        }

        return Results.Empty;
    }

    private static IResult BackToForm(AuditExportForm form, IEnumerable<Error> errors)
    {
        return Results.LocalRedirect(AdminRoutes.Audit + QueryString.Create(form.ReturnQuery(errors)));
    }

    // No text the user typed goes into the header; an item id is a GUID.
    private static string FileName(AuditQuery query, AuditExportFormat format)
    {
        var name = query.Subject is { } subject ? $"audit-history-{subject.Id:D}" : "audit-log";
        return name + (format == AuditExportFormat.Json ? ".json" : ".csv");
    }
}
