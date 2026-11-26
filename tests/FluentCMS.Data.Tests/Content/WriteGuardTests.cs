using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3568: a save, a discard or a version restore whose hook saves the same entry loses its swap to that hook, inside the
// write's transaction. A 412 would name a version that rolls back with the write, so the write is refused as the hook's
// (FCMS2405), as a publish is (#3546).
public sealed class WriteGuardTests
{
    // What a hook runs once, in the first write after it is set, with the services of that write.
    private sealed class Once
    {
        public Func<IServiceProvider, Task>? Run { get; set; }

        public async ValueTask RunAsync(IServiceProvider services)
        {
            if (Run is { } run)
            {
                Run = null;
                await run(services);
            }
        }
    }

    private sealed class ValidateHook(IServiceProvider services, Once once) : IBeforeValidateHook<ContentDocument>
    {
        public ValueTask BeforeValidateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => once.RunAsync(services);
    }

    private sealed class UpdateHook(IServiceProvider services, Once once) : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => once.RunAsync(services);
    }

    private static ServiceDatabase WithHook(Once once, bool beforeValidate) => new(cms =>
    {
        cms.Services.AddSingleton(once);
        if (beforeValidate)
        {
            cms.Services.AddScoped<IBeforeValidateHook<ContentDocument>, ValidateHook>();
        }
        else
        {
            cms.Services.AddScoped<IBeforeUpdateHook<ContentDocument>, UpdateHook>();
        }
    });

    private static IContentService Content(IServiceProvider services) => services.GetRequiredService<IContentService>();

    private static Func<IServiceProvider, Task> SavesSubtitle(Guid id, int version) =>
        services => Content(services).PatchAsync("article", id, new() { ["subtitle"] = "From the hook" }, version, Token);

    private static async Task ShouldBeRefusedAsTheHookAsync(ServiceDatabase database, Once once, Guid id, bool inCallersTransaction, Func<IContentService, Task<ContentDocument>> write)
    {
        var before = database.Trace(id);

        var refused = await Should.ThrowAsync<HookRejectedException>(() => database.AsAsync(Editor, async services =>
        {
            await using var transaction = inCallersTransaction ? await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token) : null;
            return await write(Content(services));
        }));

        refused.Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);
        once.Run.ShouldBeNull();
        database.Trace(id).ShouldBe(before);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ASaveWhoseHookSavedTheSameEntryIsRefusedAsTheHook(bool beforeValidate, bool inCallersTransaction)
    {
        var once = new Once();
        using var database = WithHook(once, beforeValidate);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        once.Run = SavesSubtitle(id, 1);

        await ShouldBeRefusedAsTheHookAsync(database, once, id, inCallersTransaction, content => content.PatchAsync("article", id, new() { ["title"] = "Two" }, 1, Token));
    }

    // The save moves a shared field, so its swap of French loses to the hook's save of French.
    [Fact]
    public async Task ASaveWhoseHookSavedAnotherLocaleOfTheEntryIsRefusedAsTheHook()
    {
        var once = new Once();
        using var database = WithHook(once, beforeValidate: false);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        once.Run = services => FrenchSubtitleAsync(services, id);

        await ShouldBeRefusedAsTheHookAsync(database, once, id, inCallersTransaction: false, content => content.PatchAsync("article", id, new() { ["views"] = 2 }, 2, Token));
    }

    // The write runs as English, so the hook's write of French names that locale itself.
    private static async Task FrenchSubtitleAsync(IServiceProvider services, Guid id)
    {
        var accessor = services.GetRequiredService<IOperationContextAccessor>();
        var outer = accessor.Current!;
        accessor.Current = outer with { Locale = "fr" };
        try
        {
            await SavesSubtitle(id, 1)(services);
        }
        finally
        {
            accessor.Current = outer;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ADiscardWhoseHookSavedTheSameEntryIsRefusedAsTheHook(bool beforeValidate, bool inCallersTransaction)
    {
        var once = new Once();
        using var database = WithHook(once, beforeValidate);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        await database.PublishAsync(Editor, id);
        await database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1);
        once.Run = SavesSubtitle(id, 2);

        await ShouldBeRefusedAsTheHookAsync(database, once, id, inCallersTransaction, content => content.DiscardDraftAsync("article", id, new LocaleWriteOptions { ExpectedVersion = 2 }, Token));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AVersionRestoreWhoseHookSavedTheSameEntryIsRefusedAsTheHook(bool beforeValidate, bool inCallersTransaction)
    {
        var once = new Once();
        using var database = WithHook(once, beforeValidate);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        await database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1);
        once.Run = SavesSubtitle(id, 2);

        await ShouldBeRefusedAsTheHookAsync(database, once, id, inCallersTransaction, content => content.RestoreVersionAsync("article", id, 1, new LocaleWriteOptions { ExpectedVersion = 2 }, Token));
    }
}
