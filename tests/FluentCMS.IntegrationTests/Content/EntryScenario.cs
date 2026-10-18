using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Content;
using FluentCMS.IntegrationTests.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Content;

// A migrated database on one provider with the entry directory acting as one user, on top of the schema scenario's types.
internal sealed class EntryScenario : IAsyncDisposable
{
    private EntryScenario(SchemaScenario schema)
    {
        Schema = schema;
    }

    public SchemaScenario Schema { get; }

    public ServiceProvider Services => Schema.Services;

    public static async Task<EntryScenario> CreateAsync(string provider, Action<IServiceCollection>? configure = null)
    {
        return new EntryScenario(await SchemaScenario.CreateAsync(provider, configure));
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IEntryDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IEntryDirectory>());
    }

    public Task<Result<Entry>> CreateAsync(string contentType, string fields)
    {
        return RunAsync(directory => directory.CreateAsync(new NewEntry(contentType, Json(fields))));
    }

    public Task<Result<Entry>> UpdateAsync(Guid id, int baseVersion, string fields)
    {
        return RunAsync(directory => directory.UpdateAsync(id, baseVersion, Json(fields)));
    }

    public async Task<List<EntryRow>> EntryRowsAsync()
    {
        return await Schema.QueryAsync(context => context.Entries.AsNoTracking().ToListAsync());
    }

    public async Task<List<EntryVersionRow>> VersionRowsAsync()
    {
        return await Schema.QueryAsync(context => context.EntryVersions.AsNoTracking().OrderBy(version => version.Version).ToListAsync());
    }

    public async Task<List<AuditEventRow>> EntryEventsAsync()
    {
        return await Schema.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "entry")
            .OrderBy(row => row.Id)
            .ToListAsync());
    }

    public async Task AssertStoredAsync(int entries, int versions, int events)
    {
        Assert.Equal(entries, (await EntryRowsAsync()).Count);
        Assert.Equal(versions, (await VersionRowsAsync()).Count);
        Assert.Equal(events, (await EntryEventsAsync()).Count);
    }

    // An entity's field as a fact with the given value and no metadata (SCH-03).
    public static string Fact(string value)
    {
        return $$"""{"value": {{value}}, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";
    }

    public static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    // PostgreSQL's jsonb keeps the meaning of a document, not its text, so fields are compared as JSON.
    public static void AssertSameFields(string expected, JsonElement actual)
    {
        Assert.True(JsonElement.DeepEquals(Json(expected), actual), $"Expected {expected} but read {actual.GetRawText()}");
    }

    public ValueTask DisposeAsync()
    {
        return Schema.DisposeAsync();
    }
}
