using FluentCMS.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Sample;

public sealed record SampleNoteRequest(string Text);

public static class SampleEndpoints
{
    // The greeting needs a signed-in caller, as every endpoint of the group does by default. The banner is public on purpose,
    // so it says so.
    public static RouteGroupBuilder MapSampleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/greeting", (SampleGreeting greeting) => Results.Ok(new { text = greeting.Text }));
        group.MapGet("/banner", (SampleGreeting greeting) => Results.Ok(new { text = greeting.Text })).AllowAnonymous();
        return group;
    }

    // Public as the Delivery API is, in the site and locale the request names. Served counts the answers the endpoint made,
    // so a copy from the output cache shows the count of the answer it kept.
    public static RouteGroupBuilder MapSampleDeliveryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/greeting", (SampleGreeting greeting, IOperationContextAccessor operations) =>
        {
            var operation = operations.Current!;
            return Results.Ok(new { text = greeting.Text, site = operation.Site.Value, locale = operation.Locale, served = greeting.Serve() });
        });
        group.MapGet("/fresh", (SampleGreeting greeting) => Results.Ok(new { served = greeting.Serve() })).CacheOutput(policy => policy.NoCache());
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
