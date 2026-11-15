using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Plugins;
using FluentCMS.Providers;
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
    private const string NotesLock = StarterPlugin.Name + ".notes";

    private static readonly TimeSpan NotesLockWait = TimeSpan.FromSeconds(10);

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

        group.MapPost("/notes", async (StarterNoteRequest request, PluginDbContext<StarterPlugin> database, IPermissionChecker permissions, IOperationContextAccessor operations, ISettings<StarterSettings> settings, IDistributedLock locks, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var site = Site(operations);
            await permissions.RequireAsync(StarterPermissions.NotesManage, new(site), cancellationToken);

            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["A note has 1 to 500 characters."] });
            }

            var max = (await settings.GetAsync(new(site), cancellationToken)).MaxNotes;

            // Posts sent together would each count the notes before any of them is saved, and pass MaxNotes between them,
            // so the count and the insert hold the plugin's lock, across every instance. The lock has one name for the
            // task, not one per site, so the posts of every site take turns.
            await using var held = await locks.TryAcquireAsync(NotesLock, NotesLockWait, cancellationToken);
            if (held is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Notes busy", detail: "Other notes were being added; try again.");
            }

            using var guarded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, held.Lost);
            if (await database.Set<StarterNote>().CountAsync(note => note.SiteId == site, guarded.Token) >= max)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Too many notes", detail: $"The site has {max} notes, the most its settings allow.");
            }

            var note = new StarterNote { Id = Guid.CreateVersion7(), SiteId = site, Text = request.Text, CreatedAt = time.GetUtcNow() };
            database.Add(note);
            await database.SaveChangesAsync(guarded.Token);
            return Results.Json(new StarterNoteResponse(note.Id, note.Text, note.CreatedAt), statusCode: StatusCodes.Status201Created);
        });

        return group;
    }

    private static Guid Site(IOperationContextAccessor operations) =>
        (operations.Current ?? throw new InvalidOperationException("The request runs outside an operation.")).Site.Value;
}
