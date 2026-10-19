using FluentCMS.Admin;
using FluentCMS.Admin.Media;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Media;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Host.Media;

// What the media library's pages cannot do themselves: take a file from a form and send bytes back. Each maps the request and calls
// the library, nothing more. Form binding makes the anti-forgery check apply to the upload (SEC-07).
internal static class MediaEndpoints
{
    // Room for the form's other fields and the multipart framing around a file of the largest size the library accepts.
    private const long FormOverhead = 1024 * 1024;

    // The largest refused body read through to answer it, so a mistaken upload of a large file gets the message.
    private const long DrainCap = 1024L * 1024 * 1024;

    // A signed-in person's upload that declares a body over the limit is sent back to the library without being parsed, the
    // anti-forgery check included, so they get the library's own message rather than a reset connection. The body is read and
    // discarded first, since a browser still sending it treats an early answer as a failure. A body declared over DrainCap, or a
    // chunked one that declares no length, still meets the server's limit.
    public static IApplicationBuilder UseMediaUploadLimit(this IApplicationBuilder app)
    {
        var limit = UploadLimit(app.ApplicationServices);
        return app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.Equals(AdminRoutes.MediaUpload, StringComparison.OrdinalIgnoreCase)
                && context.User.Identity?.IsAuthenticated == true
                && context.Request.ContentLength is { } length && length > limit && length <= DrainCap)
            {
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                {
                    size.MaxRequestBodySize = null;
                }

                await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
                context.Response.Redirect(AdminRoutes.Media + QueryString.Create(new MediaUploadForm().ReturnQuery(
                    [new Error(MediaErrorCodes.MediaTooLarge, "The file is larger than the library accepts.")])));
                return;
            }

            await next(context);
        });
    }

    public static void MapMediaLibrary(this IEndpointRouteBuilder endpoints)
    {
        var limit = UploadLimit(endpoints.ServiceProvider);

        endpoints.MapPost(AdminRoutes.MediaUpload, UploadAsync)
            .RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(limit))
            .WithFormOptions(multipartBodyLengthLimit: limit);

        // Someone whose session ran out signs in again and is sent back here with a GET; the form is on the library page.
        endpoints.MapGet(AdminRoutes.MediaUpload, () => Results.LocalRedirect(AdminRoutes.Media)).RequireAuthorization();
        endpoints.MapGet("/media/{id:guid}/file", FileAsync).RequireAuthorization();
    }

    private static async Task<IResult> UploadAsync([FromForm] MediaUploadForm? fields, IFormFile? file, IMediaLibrary library, CancellationToken cancellationToken)
    {
        // A form with only a file binds no fields at all.
        var form = fields ?? new MediaUploadForm();
        if (file is null || file.Length == 0)
        {
            return BackToLibrary(form, [new Error(MediaUploadForm.FileMissing, "Choose a file to upload.")]);
        }

        // Some browsers send the path the file came from; only its name is the asset's.
        var metadata = form.ToMetadata(Path.GetFileName(file.FileName));
        if (!metadata.IsSuccess)
        {
            return BackToLibrary(form, metadata.Errors);
        }

        await using var content = file.OpenReadStream();
        var uploaded = await library.UploadAsync(new NewAsset(content, file.ContentType, metadata.Value), cancellationToken);

        return uploaded.IsSuccess
            ? Results.LocalRedirect($"{AdminRoutes.Media}/{uploaded.Value.Id}?uploaded=true")
            : BackToLibrary(form, uploaded.Errors);
    }

    // Images show inline for the library's previews; anything else downloads. The library only stores types a browser does not run
    // as a document (MediaTypes), and nosniff keeps the browser to the stored type.
    private static async Task<IResult> FileAsync(Guid id, int? version, IMediaLibrary library, HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await library.OpenAsync(id, version, cancellationToken);
        if (!opened.IsSuccess)
        {
            return Results.NotFound();
        }

        var content = opened.Value;
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        headers.ContentDisposition = new ContentDispositionHeaderValue(content.ContentType.StartsWith("image/", StringComparison.Ordinal) ? "inline" : "attachment")
        {
            FileNameStar = content.FileName,
        }.ToString();

        return Results.Stream(content.Content, content.ContentType);
    }

    private static long UploadLimit(IServiceProvider services)
    {
        return services.GetRequiredService<IOptions<MediaOptions>>().Value.MaxBytes + FormOverhead;
    }

    private static IResult BackToLibrary(MediaUploadForm form, IEnumerable<Error> errors)
    {
        return Results.LocalRedirect(AdminRoutes.Media + QueryString.Create(form.ReturnQuery(errors)));
    }
}
