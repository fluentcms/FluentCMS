using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3265: the fields of a localized type that no locale owns. A write that moves them, or adds a locale, moves the version of
// every other locale, so a client that read one of them before is refused. A write of another locale is made to land inside
// a write here, between its read and its swaps, by a hook that runs it in the same scope and transaction.
public sealed class SharedFieldTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    // What runs once, inside the first write of the locale it waits for, as the operation of another locale.
    private sealed class Crossing
    {
        public string? When { get; set; }

        public Func<IServiceProvider, Task>? Run { get; set; }

        public string As { get; set; } = "en";
    }

    private sealed class CrossingHook(IServiceProvider services, Crossing crossing) : IBeforeUpdateHook<ContentDocument>
    {
        public async ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (crossing.Run is not { } run || context.Operation.Locale != crossing.When)
            {
                return;
            }

            crossing.Run = null;
            var accessor = services.GetRequiredService<IOperationContextAccessor>();
            var outer = accessor.Current!;
            accessor.Current = outer with { Locale = crossing.As };
            try
            {
                await run(services);
            }
            finally
            {
                accessor.Current = outer;
            }
        }
    }

    private static ServiceDatabase WithCrossing(Crossing crossing) => new(cms =>
    {
        cms.Services.AddSingleton(crossing);
        cms.Services.AddScoped<IBeforeUpdateHook<ContentDocument>, CrossingHook>();
    });

    private static IContentService Content(IServiceProvider services) => services.GetRequiredService<IContentService>();

    // English at version 2, since adding French moved it, and French at version 1.
    private static async Task<Guid> EnglishAndFrenchAsync(ServiceDatabase database)
    {
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        return id;
    }

    [Fact]
    public async Task AddingALocaleAdvancesTheOtherLocales()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, Titled("Hello")));

        await database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");

        var english = database.Versions(id).Where(version => version.Locale == "en").ToList();
        english.Select(version => version.Number).ShouldBe([1, 2]);
        english[^1].Message.ShouldBe("Added the locale fr.");
        await Should.ThrowAsync<VersionConflictException>(() => database.PatchAsync(Editor, id, new() { ["views"] = 3 }, 1));
    }

    // A adds fr with the shared values as it read them, and B changes one in en in between: A must not write it back.
    [Fact]
    public async Task AddingALocaleCannotWriteBackASharedValueChangedSinceItsRead()
    {
        var crossing = new Crossing { When = "fr", As = "en" };
        using var database = WithCrossing(crossing);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        crossing.Run = services => Content(services).PatchAsync("article", id, new() { ["views"] = 7 }, 1, Token);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["views"] = 1 }, 0, locale: "fr"));

        crossing.Run.ShouldBeNull();
        conflict.ExpectedVersion.ShouldBe(0);
        database.Versions(id).ShouldAllBe(version => version.Locale == "en");
    }

    // B read [en] and changes a shared field, and A adds fr in between: B would leave fr behind, so B is refused.
    [Fact]
    public async Task ASharedChangeThatMissedALocaleAddedSinceItsReadConflicts()
    {
        var crossing = new Crossing { When = "en", As = "fr" };
        using var database = WithCrossing(crossing);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        crossing.Run = services => Content(services).UpdateAsync("article", id, Titled("Bonjour"), 0, Token);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.PatchAsync(Editor, id, new() { ["views"] = 5 }, 1));

        crossing.Run.ShouldBeNull();
        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((1, 2));
    }

    // The client's own version is still valid; another locale's moved since the write read it.
    [Fact]
    public async Task ASharedChangeIsRefusedWhenAnotherLocaleMovedSinceItsReadThoughItsOwnVersionHolds()
    {
        var crossing = new Crossing { When = "en", As = "fr" };
        using var database = WithCrossing(crossing);
        var id = await EnglishAndFrenchAsync(database);
        crossing.Run = services => Content(services).PatchAsync("article", id, Titled("Salut"), 1, Token);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.PatchAsync(Editor, id, new() { ["views"] = 5 }, 2));

        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((2, 2));
        (await database.GetAsync(Editor, id))!["views"].ShouldBe(1L);
    }

    // A publish of fr commits between the write's read and its swap of fr: the status is decided by fr as it is then.
    [Fact]
    public async Task APublishOfAnotherLocaleBetweenTheReadAndTheSwapKeepsItsStatus()
    {
        var crossing = new Crossing { When = "en", As = "en" };
        using var database = WithCrossing(crossing);
        var id = await EnglishAndFrenchAsync(database);
        crossing.Run = services => Content(services).PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr" }, Token);

        await database.PatchAsync(Editor, id, new() { ["views"] = 5 }, 2);

        crossing.Run.ShouldBeNull();
        var french = database.Document(id, "fr");
        french.PublishedVersion.ShouldNotBeNull();
        french.Status.ShouldBe(EntryStatus.Modified);
        french.DraftVersion.ShouldBe(2);
    }

    [Fact]
    public async Task AnUnpublishOfAnotherLocaleBetweenTheReadAndTheSwapKeepsItsStatus()
    {
        var crossing = new Crossing { When = "en", As = "en" };
        using var database = WithCrossing(crossing);
        var id = await EnglishAndFrenchAsync(database);
        await database.PublishAsync(Editor, id, "fr");
        crossing.Run = services => Content(services).UnpublishAsync("article", id, new LocaleOptions { Locale = "fr" }, Token);

        await database.PatchAsync(Editor, id, new() { ["views"] = 5 }, 2);

        crossing.Run.ShouldBeNull();
        var french = database.Document(id, "fr");
        french.PublishedVersion.ShouldBeNull();
        french.Status.ShouldBe(EntryStatus.Unpublished);
        french.DraftVersion.ShouldBe(2);
    }

    [Fact]
    public async Task RestoringAVersionThatMovesTheSharedFieldsAdvancesTheOtherLocales()
    {
        using var database = new ServiceDatabase();
        var id = await EnglishAndFrenchAsync(database);
        await database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 2);

        await database.RestoreVersionAsync(Editor, id, 2, expected: 3);

        var french = database.Versions(id).Where(version => version.Locale == "fr").ToList();
        french.Select(version => version.Number).ShouldBe([1, 2, 3]);
        french[^1].Message.ShouldBe("Shared fields changed in en.");
        JsonNode.Parse(french[^1].Snapshot)!["views"]!.GetValue<long>().ShouldBe(1);
    }

    private sealed class SetViews : IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["views"] = 42;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task APublishWhoseHooksChangeASharedFieldAdvancesTheOtherLocales()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>, SetViews>());
        var id = await EnglishAndFrenchAsync(database);

        await database.PublishAsync(Editor, id);

        database.Document(id, "fr").DraftVersion.ShouldBe(2);
        (await database.GetAsync(Editor, id, locale: "fr"))!["views"].ShouldBe(42L);
        await Should.ThrowAsync<VersionConflictException>(() => database.PatchAsync(Editor, id, new() { ["views"] = 3 }, 1, locale: "fr"));
    }
}
