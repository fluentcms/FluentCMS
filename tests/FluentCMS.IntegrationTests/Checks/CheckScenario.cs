using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Checks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Checks;

// A migrated database on one provider with the check directory acting as whoever Initiator names, and two sample check types.
internal sealed class CheckScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    public static readonly AgentInitiator Agent = new(Guid.NewGuid(), "example-agent");

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;

    private CheckScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
    }

    public ServiceProvider Services { get; }

    public Initiator? Initiator
    {
        get => _initiator.Current;
        set => _initiator.Current = value;
    }

    public static async Task<CheckScenario> CreateAsync(
        string provider,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configure = null)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Amir };
        var services = TestServices.Build(
            new Dictionary<string, string?>(database.Configuration.Concat(settings ?? [])),
            collection =>
            {
                collection
                    .AddSingleton<ICurrentInitiator>(initiator)
                    .AddFluentCmsExtension<ICheckType, BannedWordsCheckType>()
                    .AddFluentCmsExtension<ICheckType, DefinedFieldsCheckType>();
                configure?.Invoke(collection);
            });

        await TestServices.MigrateAsync(services);
        return new CheckScenario(database, services, initiator);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<ICheckDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICheckDirectory>());
    }

    // The winner runs inside a unit of work that stays open until the loser has read what it needs and is waiting to write.
    public async Task<Result<Check>> RaceAsync(Func<ICheckDirectory, Task<Result<Check>>> winner, Func<ICheckDirectory, Task<Result<Check>>> loser)
    {
        await using var scope = Services.CreateAsyncScope();
        var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

        Assert.True((await winner(scope.ServiceProvider.GetRequiredService<ICheckDirectory>())).IsSuccess);

        var second = Task.Run(() => RunAsync(loser));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await work.CommitAsync();
        await work.DisposeAsync();

        return await second;
    }

    public Task<Result<Check>> CreateAsync(string name, CheckDefinition definition)
    {
        return RunAsync(directory => directory.CreateAsync(new NewCheck(name, definition)));
    }

    public async Task<Result<ContentType>> CreateContentTypeAsync(string slug, params FieldDefinition[] fields)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>()
            .CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition(fields)), TestChangeSetTargets.ContentModel);
    }

    public async Task<List<AuditEventRow>> AuditRowsAsync()
    {
        return await QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.SubjectType == "check").OrderBy(row => row.Id).ToListAsync());
    }

    public async Task<List<CheckVersionRow>> VersionRowsAsync()
    {
        return await QueryAsync(context => context.CheckVersions.AsNoTracking().OrderBy(row => row.Version).ToListAsync());
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _database.DisposeAsync();
    }

    // "Best in class" is the claim the reference scenario's checks refuse (02-personas-and-scenarios, S2).
    public static CheckDefinition NoSuperlatives(string word = "best", params CheckExample[] extra)
    {
        return new CheckDefinition(
            "sample.bannedWords",
            "Product copy makes no unsupported superlative claims.",
            Parse($$"""{"words": ["{{word}}"]}"""),
            [
                Example("plain copy", CheckVerdict.Pass, """{"headline": "A sturdy kettle"}"""),
                Example($"says {word} in class", CheckVerdict.Fail, $$"""{"headline": "The {{word}} in class kettle"}"""),
                .. extra,
            ]);
    }

    public static CheckExample Example(string name, CheckVerdict expected, string fields, string? contentType = null)
    {
        return new CheckExample(name, expected, Parse(fields), contentType: contentType);
    }

    public static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private sealed class SwitchableInitiator : ICurrentInitiator
    {
        public Initiator? Current { get; set; }
    }
}

// Fails every string field that contains one of its words, ignoring case.
internal sealed class BannedWordsCheckType : ICheckType
{
    public string Name => "sample.bannedWords";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        reader.RequiredStringList("words");
        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var words = new CheckSettingsReader(Name, input.Settings).RequiredStringList("words") ?? [];
        var findings = input.Subject.Fields.ValueKind != JsonValueKind.Object
            ? []
            : input.Subject.Fields.EnumerateObject()
                .Where(field => field.Value.ValueKind == JsonValueKind.String)
                .SelectMany(field => words
                    .Where(word => field.Value.GetString()!.Contains(word, StringComparison.OrdinalIgnoreCase))
                    .Select(word => new CheckFinding($"Remove '{word}' from {field.Name}.", $"/{field.Name}", word)))
                .ToList();

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }
}

// Fails every field its content type does not define, so a test sees which definition reached the type.
internal sealed class DefinedFieldsCheckType : ICheckType
{
    public string Name => "sample.definedFields";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        return new CheckSettingsReader(Name, settings).Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        if (input.Subject.Definition is not { } definition || input.Subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(CheckOutcome.NeedsReview("The content type is not known."));
        }

        var findings = input.Subject.Fields.EnumerateObject()
            .Where(property => !definition.HasField(property.Name))
            .Select(property => new CheckFinding($"Remove '{property.Name}', which {input.Subject.ContentType} does not define.", $"/{property.Name}"))
            .ToList();

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }
}
