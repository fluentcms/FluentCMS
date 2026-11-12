using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Plugins;
using FluentCMS.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FluentCms_Plugin1;

public sealed record StarterNoteRequest(string Text);

public sealed record StarterNoteResponse(Guid Id, string Text, DateTimeOffset CreatedAt);

public static class StarterEndpoints
{
    // The group checks that the caller has the management scope; what the caller may do is the plugin's to check. Each
    // request runs in the operation of its site, so the notes are the site's.
    public static RouteGroupBuilder MapStarterEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/notes", async (PluginDbContext<StarterPlugin> database, IPermissionChecker permissions, IOperationContextAccessor operations, CancellationToken cancellationToken) =>
        {
            var site = Site(operations);
            await permissions.RequireAsync(StarterPermissions.NotesManage, new(site), cancellationToken);

            var notes = await database.Set<StarterNote>()
                .Where(note => note.SiteId == site)
                .OrderBy(note => note.CreatedAt)
                .Select(note => new StarterNoteResponse(note.Id, note.Text, note.CreatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(notes);
        });

        group.MapPost("/notes", async (StarterNoteRequest request, PluginDbContext<StarterPlugin> database, IPermissionChecker permissions, IOperationContextAccessor operations, ISettings<StarterSettings> settings, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var site = Site(operations);
            await permissions.RequireAsync(StarterPermissions.NotesManage, new(site), cancellationToken);

            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["A note has 1 to 500 characters."] });
            }

            var max = (await settings.GetAsync(new(site), cancellationToken)).MaxNotes;
            if (await database.Set<StarterNote>().CountAsync(note => note.SiteId == site, cancellationToken) >= max)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Too many notes", detail: $"The site has {max} notes, the most its settings allow.");
            }

            var note = new StarterNote { Id = Guid.CreateVersion7(), SiteId = site, Text = request.Text, CreatedAt = time.GetUtcNow() };
            database.Add(note);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Json(new StarterNoteResponse(note.Id, note.Text, note.CreatedAt), statusCode: StatusCodes.Status201Created);
        });

        return group;
    }

    private static Guid Site(IOperationContextAccessor operations) =>
        (operations.Current ?? throw new InvalidOperationException("The request runs outside an operation.")).Site.Value;
}
