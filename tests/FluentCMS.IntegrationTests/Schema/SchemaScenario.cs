using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Schema;

// A migrated database on one provider with the content type directory acting as one user.
internal sealed class SchemaScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    private readonly TemporaryDatabase _database;

    private SchemaScenario(TemporaryDatabase database, ServiceProvider services)
    {
        _database = database;
        Services = services;
    }

    public ServiceProvider Services { get; }

    public static async Task<SchemaScenario> CreateAsync(string provider, Action<IServiceCollection>? configure = null)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var services = TestServices.Build(
            database.Configuration,
            collection =>
            {
                collection.AddScoped<ICurrentInitiator>(_ => new TestInitiator(Amir));
                configure?.Invoke(collection);
            });

        await TestServices.MigrateAsync(services);
        return new SchemaScenario(database, services);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IContentTypeDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>());
    }

    public Task<Result<ContentType>> CreateAsync(string slug, params FieldDefinition[] fields)
    {
        return RunAsync(directory => directory.CreateAsync(new NewContentType(Title(slug), slug, new ContentTypeDefinition(fields))));
    }

    public Task<Result<ContentType>> CreateEntityAsync(string slug, params FieldDefinition[] fields)
    {
        return RunAsync(directory => directory.CreateAsync(new NewContentType(Title(slug), slug, new ContentTypeDefinition(fields, isEntity: true))));
    }

    public Task<Result<ContentType>> ReviseAsync(string slug, int baseVersion, ContentTypeDefinition definition)
    {
        return RunAsync(directory => directory.ReviseAsync(slug, baseVersion, definition));
    }

    public async Task<List<ContentTypeRow>> ContentTypeRowsAsync()
    {
        return await QueryAsync(context => context.ContentTypes.AsNoTracking().ToListAsync());
    }

    public async Task<List<ContentTypeVersionRow>> VersionRowsAsync()
    {
        return await QueryAsync(context => context.ContentTypeVersions.AsNoTracking().OrderBy(version => version.Version).ToListAsync());
    }

    public async Task<List<AuditEventRow>> AuditRowsAsync()
    {
        return await QueryAsync(context => context.AuditEvents.AsNoTracking().ToListAsync());
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

    // The model behind the reference scenario (02-personas-and-scenarios): four entity types and the four page types that use them.
    public static readonly (string Slug, bool IsEntity, FieldDefinition[] Fields)[] ReferenceModel =
    [
        ("category", true, [Field("name", "text", required: true), Reference("parent", "category")]),
        ("region", true, [Field("name", "text", required: true), Field("code", "text", """{"maxLength": 8}""")]),
        ("spec", true, [Field("name", "text", required: true), Field("value", "text"), Field("unit", "enum", """{"values": ["mm", "kg", "W"]}""")]),
        ("product", true,
        [
            Field("name", "text", """{"maxLength": 200}""", required: true),
            Field("price", "number", """{"min": 0, "max": 99999.99}""", required: true, description: "The list price, before discounts."),
            Field("tags", "list", """{"itemType": "text"}"""),
            Reference("category", "category"),
            References("specs", "spec"),
            References("regions", "region"),
            Field("hero", "media"),
        ]),
        ("product-page", false, [Reference("product", "product"), Reference("price", "product", "price"), Field("headline", "text"), Field("body", "richText")]),
        ("category-page", false, [Reference("category", "category"), Field("intro", "richText")]),
        ("comparison-page", false, [References("products", "product"), References("prices", "product", "price"), Field("verdict", "richText"), Field("extra", "json")]),
        ("blog-post", false, [Field("title", "text", required: true), Field("body", "richText"), Field("publishedOn", "dateTime"), Field("featured", "boolean"), References("related", "product")]),
    ];

    public async Task CreateReferenceModelAsync()
    {
        foreach (var (slug, isEntity, fields) in ReferenceModel)
        {
            var created = isEntity ? await CreateEntityAsync(slug, fields) : await CreateAsync(slug, fields);
            Assert.True(created.IsSuccess, $"{slug}: {string.Join("; ", created.Errors.Select(error => error.Message))}");
        }
    }

    public static FieldDefinition Field(string name, string type, string settings = "{}", bool required = false, string? description = null)
    {
        return new FieldDefinition(name, type, required, JsonSerializer.Deserialize<JsonElement>(settings), description);
    }

    public static FieldDefinition Reference(string name, string target, string? field = null)
    {
        return Field(name, "reference", ReferenceSettings(target, field));
    }

    public static FieldDefinition References(string name, string target, string? field = null)
    {
        return Field(name, "list", $$"""{"itemType": "reference", "itemSettings": {{ReferenceSettings(target, field)}}}""");
    }

    private static string ReferenceSettings(string target, string? field)
    {
        return field is null ? $$"""{"target": "{{target}}"}""" : $$"""{"target": "{{target}}", "field": "{{field}}"}""";
    }

    private static string Title(string slug)
    {
        return string.Join(' ', slug.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
