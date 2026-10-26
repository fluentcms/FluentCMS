using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Builtin.Tools;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Mcp;

// A schema tool answers with the set its write joined, read by id, so what happens to the session between the write and the answer,
// such as a submit or the session's next set, cannot change or break the answer.
public sealed class SchemaWriteAnswerTests
{
    private const string Session = "catalog-model";

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_write_whose_set_is_submitted_before_the_answer_answers_with_that_set_in_its_new_state(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider, async (services, joined) =>
            Assert.True((await services.GetRequiredService<IChangeSetDirectory>().TransitionAsync(joined, ChangeSetState.ChecksRunning)).IsSuccess));

        var answer = await scenario.CreateAsync("product");

        var joined = Assert.Single(scenario.Joined);
        Assert.Equal(joined.ToString(), (string?)answer["changeSet"]!["id"]);
        Assert.Equal("checks_running", (string?)answer["changeSet"]!["state"]);
        Assert.Equal(1, (int?)answer["contentType"]!["version"]);
        Assert.Contains("submitted since", (string?)answer["next"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_write_whose_session_opened_its_next_set_meanwhile_answers_with_the_set_it_joined(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider, async (services, joined) =>
        {
            var changeSets = services.GetRequiredService<IChangeSetDirectory>();
            Assert.True((await changeSets.TransitionAsync(joined, ChangeSetState.ChecksRunning)).IsSuccess);
            Assert.True((await services.GetRequiredService<IContentTypeDirectory>().CreateAsync(
                new NewContentType("Category", "category", new ContentTypeDefinition([new FieldDefinition("name", "text")])),
                ChangeSetTarget.Session(Session, new NewChangeSet("The next model change.", [])))).IsSuccess);
        });

        var answer = await scenario.CreateAsync("product");

        var joined = scenario.Joined[0];
        Assert.Equal(joined.ToString(), (string?)answer["changeSet"]!["id"]);
        Assert.Equal("product", (string?)answer["contentType"]!["slug"]);
        Assert.NotEqual(joined, (await scenario.SessionSetAsync()).Id);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_whose_set_is_submitted_before_the_answer_answers_with_that_set_in_its_new_state(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider, async (services, joined) =>
            Assert.True((await services.GetRequiredService<IChangeSetDirectory>().TransitionAsync(joined, ChangeSetState.ChecksRunning)).IsSuccess));
        scenario.Racing = false;
        var created = await scenario.CreateAsync("product");
        scenario.Racing = true;

        var answer = await scenario.ReviseAsync("product", 1);

        var joined = Assert.Single(scenario.Joined);
        Assert.Equal((string?)created["changeSet"]!["id"], joined.ToString());
        Assert.Equal(joined.ToString(), (string?)answer["changeSet"]!["id"]);
        Assert.Equal("checks_running", (string?)answer["changeSet"]!["state"]);
        Assert.Equal(2, (int?)answer["contentType"]!["version"]);
        Assert.Contains("submitted since", (string?)answer["next"], StringComparison.Ordinal);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly TemporaryDatabase _database;
        private readonly ServiceProvider _services;
        private readonly RacingContentTypes.Shared _shared;

        private Scenario(TemporaryDatabase database, ServiceProvider services, RacingContentTypes.Shared shared)
        {
            _database = database;
            _services = services;
            _shared = shared;
        }

        public IReadOnlyList<Guid> Joined => _shared.Joined;

        // Whether the next write lets the other call in.
        public bool Racing
        {
            get => _shared.Racing;
            set => _shared.Racing = value;
        }

        // What another call does right after the tool's write commits and before the tool answers, in a scope of its own.
        public static async Task<Scenario> CreateAsync(string provider, Func<IServiceProvider, Guid, Task> meanwhile)
        {
            var database = await TemporaryDatabase.CreateAsync(provider);
            var shared = new RacingContentTypes.Shared(meanwhile);
            var services = TestServices.Build(
                database.Configuration,
                collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(new UserInitiator(Guid.NewGuid(), "Amir"))),
                collection =>
                {
                    var original = collection.Single(descriptor => descriptor.ServiceType == typeof(IContentTypeDirectory));
                    collection.Remove(original);
                    collection.AddScoped<IContentTypeDirectory>(provider => new RacingContentTypes(
                        (IContentTypeDirectory)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!),
                        provider.GetRequiredService<IServiceScopeFactory>(),
                        shared));
                });
            await TestServices.MigrateAsync(services);
            return new Scenario(database, services, shared);
        }

        public Task<JsonObject> CreateAsync(string slug)
        {
            return CallAsync("create_content_type", new Dictionary<string, object>
            {
                ["name"] = slug,
                ["slug"] = slug,
                ["definition"] = new { fields = new[] { new { name = "title", type = "text" } } },
                ["session_id"] = Session,
                ["rationale"] = "Model the catalog.",
            });
        }

        public Task<JsonObject> ReviseAsync(string slug, int baseVersion)
        {
            return CallAsync("revise_content_type", new Dictionary<string, object>
            {
                ["slug"] = slug,
                ["base_version"] = baseVersion,
                ["definition"] = new { fields = new[] { new { name = "title", type = "text" }, new { name = "sku", type = "text" } } },
                ["session_id"] = Session,
                ["rationale"] = "Give products a SKU.",
            });
        }

        private async Task<JsonObject> CallAsync(string name, Dictionary<string, object> arguments)
        {
            await using var scope = _services.CreateAsyncScope();
            var tool = new SchemaTools().Tools.Single(candidate => candidate.Name == name);

            var result = await tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), scope.ServiceProvider));

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
            return result.Value!.AsObject();
        }

        public async Task<ChangeSet> SessionSetAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().GetSessionSetAsync(Session)).Value;
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    // The directory as the product registers it, with another call let in after the first racing write of a test commits.
    private sealed class RacingContentTypes(IContentTypeDirectory inner, IServiceScopeFactory scopes, RacingContentTypes.Shared shared) : IContentTypeDirectory
    {
        public Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);

        public Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default) => inner.GetAsync(slug, cancellationToken);

        public Task<Result<ContentType>> GetPublishedAsync(string slug, CancellationToken cancellationToken = default) => inner.GetPublishedAsync(slug, cancellationToken);

        public Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default) =>
            inner.GetVersionAsync(slug, version, cancellationToken);

        public Task<Result<ContentType>> CreateAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(contentType, changeSet, cancellationToken);

        public Task<Result<ContentType>> ReviseAsync(string slug, int baseVersion, ContentTypeDefinition definition, ChangeSetTarget changeSet, CancellationToken cancellationToken = default) =>
            inner.ReviseAsync(slug, baseVersion, definition, changeSet, cancellationToken);

        public async Task<Result<ContentTypeWrite>> CreateInChangeSetAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default)
        {
            return await RaceAsync(await inner.CreateInChangeSetAsync(contentType, changeSet, cancellationToken));
        }

        public async Task<Result<ContentTypeWrite>> ReviseInChangeSetAsync(
            string slug,
            int baseVersion,
            ContentTypeDefinition definition,
            ChangeSetTarget changeSet,
            CancellationToken cancellationToken = default)
        {
            return await RaceAsync(await inner.ReviseInChangeSetAsync(slug, baseVersion, definition, changeSet, cancellationToken));
        }

        private async Task<Result<ContentTypeWrite>> RaceAsync(Result<ContentTypeWrite> written)
        {
            if (written.IsSuccess && shared.Racing && shared.Joined.Count == 0)
            {
                shared.Joined.Add(written.Value.ChangeSetId);
                await using var scope = scopes.CreateAsyncScope();
                await shared.Meanwhile(scope.ServiceProvider, written.Value.ChangeSetId);
            }

            return written;
        }

        public sealed class Shared(Func<IServiceProvider, Guid, Task> meanwhile)
        {
            public Func<IServiceProvider, Guid, Task> Meanwhile { get; } = meanwhile;

            public bool Racing { get; set; } = true;

            public List<Guid> Joined { get; } = [];
        }
    }
}
