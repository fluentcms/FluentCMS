using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Jobs;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Desk;

// Sets in the states the checks card shows, made through the code that makes them.
internal static class CheckedSets
{
    // A product page whose headline says "best in class", submitted while a terms check against it is active: its run fails.
    public static async Task<Guid> FailingAsync(AdminHost host, Guid ownerId)
    {
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(
            new NewContentType("Product page", "product-page", new ContentTypeDefinition([new FieldDefinition("headline", "text")])),
            TestChangeSetTargets.ContentModel)).Value);
        await host.AsUserAsync<ICheckDirectory, Check>(ownerId, async checks =>
        {
            var created = (await checks.CreateAsync(new NewCheck("no-superlatives", new CheckDefinition(
                "terms",
                "Headlines make no claims they cannot prove.",
                Json("""{"terms": ["best in class"]}"""),
                [
                    new CheckExample("passes", CheckVerdict.Pass, Json("""{"headline": "A sturdy kettle"}"""), contentType: "product-page"),
                    new CheckExample("fails", CheckVerdict.Fail, Json("""{"headline": "The best in class kettle"}"""), contentType: "product-page"),
                ],
                scope: new CheckScope(["product-page"], null))))).Value;
            return (await checks.ActivateAsync(created.Name, created.Version)).Value;
        });
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product-page", Json("""{"headline": "The best in class kettle"}""")))).Value);

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add the kettle page.", []))).Value;
            return (await directory.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value)).Value;
        });
        return await host.AsUserAsync<IChangeSetChecks, Guid>(ownerId, async checks => (await checks.SubmitAsync(set.Id)).Value.ChangeSet.Id);
    }

    // A set a person submitted whose check job ended without moving it, as when every attempt failed.
    public static async Task<Guid> DeadJobAsync(AdminHost host, Guid ownerId, ContentType type)
    {
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add the brand type.", []))).Value;
            await directory.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            return (await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning)).Value;
        });

        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        var now = DateTimeOffset.UtcNow;
        context.Add(new JobRow
        {
            Id = Guid.CreateVersion7(),
            Type = "checks.run",
            Payload = JsonSerializer.Serialize(new { changeSetId = set.Id }),
            SubjectType = "change_set",
            SubjectId = set.Id,
            State = JobState.Dead,
            Attempts = 6,
            MaxAttempts = 6,
            CreatedAt = now,
            ScheduledAt = now,
            FinishedAt = now,
        });
        await context.SaveChangesAsync();
        return set.Id;
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
