using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Sample;

public sealed record SampleNoteRequest(string Text);

public static class SampleEndpoints
{
    public static RouteGroupBuilder MapSampleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/greeting", (SampleGreeting greeting) => Results.Ok(new { text = greeting.Text }));
        return group;
    }

    // A note's text is unique, so a second one with the same text is refused with the plugin's own code.
    public static RouteGroupBuilder MapSampleManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/notes", async (PluginDbContext<SamplePlugin> database, CancellationToken cancellationToken) =>
            Results.Ok(await database.Set<SampleNote>().OrderBy(note => note.CreatedAt).Select(note => note.Text).ToListAsync(cancellationToken)));

        group.MapPost("/notes", async (SampleNoteRequest request, PluginDbContext<SamplePlugin> database, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (await database.Set<SampleNote>().AnyAsync(note => note.Text == request.Text, cancellationToken))
            {
                throw new PluginException(SamplePlugin.NoteExists, $"A note '{request.Text}' exists.");
            }

            var note = new SampleNote { Id = Guid.CreateVersion7(), Text = request.Text, CreatedAt = time.GetUtcNow() };
            database.Add(note);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"notes/{note.Id}", new { id = note.Id, text = note.Text });
        });

        return group;
    }
}
