using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

internal sealed record AssetMove(Guid? FolderId);

internal sealed record FolderCreate(string? Name, Guid? ParentId);

internal sealed record FolderRename(string? Name);

internal sealed record FolderMove(Guid? ParentId);

// /api/management/v1/media. The service checks the media permissions for the site of the request; an upload reads the
// multipart body here, bounded by FluentCms:Media:MaxRequestBodySize, and hands its files to the service.
internal static class MediaEndpoints
{
    public const string RootFolder = "root";

    public static RouteGroupBuilder MapMediaEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (string? folder, bool? subfolders, string? type, string? tag, int? page, int? pageSize, IMediaService media, CancellationToken cancellationToken) =>
        {
            var query = new MediaQuery
            {
                FolderId = folder is null || folder == RootFolder ? null : Guid.TryParse(folder, out var id) ? id : throw Invalid("folder", $"A folder is an id or '{RootFolder}'."),
                RootOnly = folder == RootFolder,
                IncludeSubfolders = subfolders ?? false,
                MimeType = type,
                Tag = tag,
                Page = page ?? 1,
                PageSize = pageSize ?? 50,
            };
            var result = await media.ListAsync(query, cancellationToken);
            return Results.Ok(new { items = result.Items, total = result.Total, page = result.Page, pageSize = result.PageSize });
        });

        group.MapPost("", UploadAsync);

        group.MapGet("/{id:guid}", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.GetAsync(id, cancellationToken)));

        group.MapPatch("/{id:guid}", async (Guid id, MediaAssetChanges changes, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.UpdateAsync(id, changes, cancellationToken)));

        group.MapPost("/{id:guid}/move", async (Guid id, AssetMove move, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.MoveAsync(id, move.FolderId, cancellationToken)));

        group.MapDelete("/{id:guid}", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
        {
            await media.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/folders", async (IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await media.ListFoldersAsync(cancellationToken) }));

        group.MapPost("/folders", async (FolderCreate request, IMediaService media, CancellationToken cancellationToken) =>
        {
            var created = await media.CreateFolderAsync(request.Name ?? "", request.ParentId, cancellationToken);
            return Results.Created($"media/folders/{created.Id:D}", created);
        });

        group.MapPatch("/folders/{id:guid}", async (Guid id, FolderRename request, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.RenameFolderAsync(id, request.Name ?? "", cancellationToken)));

        group.MapPost("/folders/{id:guid}/move", async (Guid id, FolderMove request, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.MoveFolderAsync(id, request.ParentId, cancellationToken)));

        group.MapDelete("/folders/{id:guid}", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
        {
            await media.DeleteFolderAsync(id, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    // Every file part of the form is a file of the upload, whatever its field name; the field folderId names the folder.
    private static async Task<IResult> UploadAsync(HttpRequest request, IMediaService media, IOptions<MediaOptions> options, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw Invalid("files", "An upload is a multipart/form-data request with one or more files.");
        }

        var limit = options.Value.MaxRequestBodySize;
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit, BufferBodyLengthLimit = limit }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException || (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }))
        {
            throw new PluginException(MediaErrorCodes.TooLarge, $"The upload is over the limit of {limit} bytes a request.");
        }

        Guid? folderId = form["folderId"].ToString() is { Length: > 0 } folder
            ? Guid.TryParse(folder, out var id) ? id : throw Invalid("folderId", "The folder is an id.")
            : null;
        List<Stream> streams = [];
        try
        {
            List<MediaUploadFile> files = [];
            foreach (var file in form.Files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new MediaUploadFile(file.FileName, stream));
            }

            var created = await media.UploadAsync(new MediaUpload(folderId, files), cancellationToken);
            return Results.Created((string?)null, new { items = created });
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static ValidationException Invalid(string path, string message) => MediaService.Invalid(path, message);
}
