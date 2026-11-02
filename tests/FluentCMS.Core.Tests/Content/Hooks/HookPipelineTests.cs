using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Hooks;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Hooks;

public class HookPipelineTests
{
    private sealed class Log
    {
        public List<string> Entries { get; } = [];

        public List<HookContext<ContentDocument>> Contexts { get; } = [];
    }

    private abstract class RecordingHook(Log log, string name)
    {
        protected ValueTask Record(HookContext<ContentDocument> context)
        {
            log.Entries.Add(name);
            log.Contexts.Add(context);
            return ValueTask.CompletedTask;
        }
    }

    [HookOrder(10)]
    private sealed class Late(Log log) : RecordingHook(log, "late"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record(context);
    }

    private sealed class Default(Log log) : RecordingHook(log, "default"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record(context);
    }

    [HookOrder(-5)]
    private sealed class Early(Log log) : RecordingHook(log, "early"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record(context);
    }

    private sealed class PluginA(Log log) : RecordingHook(log, "A"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record(context);
    }

    private sealed class PluginB(Log log) : RecordingHook(log, "B"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record(context);
    }

    private sealed class RankByType(Dictionary<Type, int> ranks) : IHookTiebreaker
    {
        public int Rank(Type hookType) => ranks.GetValueOrDefault(hookType);
    }

    private sealed class Rejecting(Log log) : RecordingHook(log, "rejecting"), IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Cancel("ACME0100", "Not today.");
            return Record(context);
        }
    }

    private sealed class Throwing : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) =>
            throw new HookRejectedException("ACME0101", "Rejected directly.");
    }

    private sealed class RejectingAfter : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Cancel("ACME0102", "Too late.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Mutating : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["title"] = context.Entry.Get<string>("title")!.ToUpperInvariant();
            context.Entry["addedByHook"] = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SeesTitle(Log log) : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            log.Entries.Add(context.Entry.Get<string>("title")!);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CaptureUpdate(Log log) : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            log.Contexts.Add(context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TypedRetitle(Log log) : IBeforeUpdateHook<Article>
    {
        public ValueTask BeforeUpdateAsync(HookContext<Article> context, CancellationToken cancellationToken)
        {
            log.Entries.Add($"typed:{context.Previous?.Title}->{context.Entry.Title}");
            context.Entry.Title += " (typed)";
            context.Entry.Slug = "set-by-typed-hook";
            return ValueTask.CompletedTask;
        }
    }

    [HookOrder(-1)]
    private sealed class EarlyTyped(Log log) : IBeforeUpdateHook<Article>
    {
        public ValueTask BeforeUpdateAsync(HookContext<Article> context, CancellationToken cancellationToken)
        {
            log.Entries.Add("typed-early");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BaseClassHook(Log log) : IBeforeUpdateHook<ContentItem>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentItem> context, CancellationToken cancellationToken)
        {
            log.Entries.Add($"base:{context.Entry.GetType().Name}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AllTypes(Log log) : IBeforeUpdateHook<ContentDocument>, IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            log.Entries.Add($"doc:{context.Entry.Get<string>("title")}");
            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            log.Entries.Add("doc");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EveryKind(Log log) :
        IBeforeValidateHook<ContentDocument>,
        IBeforeCreateHook<ContentDocument>, IAfterCreateHook<ContentDocument>,
        IBeforeUpdateHook<ContentDocument>, IAfterUpdateHook<ContentDocument>,
        IBeforeDeleteHook<ContentDocument>, IAfterDeleteHook<ContentDocument>,
        IBeforePublishHook<ContentDocument>, IAfterPublishHook<ContentDocument>,
        IBeforeUnpublishHook<ContentDocument>, IAfterUnpublishHook<ContentDocument>,
        IBeforeRestoreHook<ContentDocument>, IAfterRestoreHook<ContentDocument>
    {
        private ValueTask Note(HookKind kind)
        {
            log.Entries.Add(kind.ToString());
            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeValidateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeValidate);

        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeCreate);

        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterCreate);

        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeUpdate);

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterUpdate);

        public ValueTask BeforeDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeDelete);

        public ValueTask AfterDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterDelete);

        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforePublish);

        public ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterPublish);

        public ValueTask BeforeUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeUnpublish);

        public ValueTask AfterUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterUnpublish);

        public ValueTask BeforeRestoreAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.BeforeRestore);

        public ValueTask AfterRestoreAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Note(HookKind.AfterRestore);
    }

    private static ContentDocument Doc(string title = "Hello") => new() { ["title"] = title };

    private static Action<IServiceCollection> Create(Log log, params Type[] hooks) => services =>
    {
        services.AddSingleton(log);
        foreach (var hook in hooks)
        {
            services.AddSingleton(typeof(IBeforeCreateHook<ContentDocument>), hook);
        }
    };

    [Fact]
    public async Task AHookSeesTheOperationTheEntryThePreviousStateAndTheChangedPaths()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(services =>
        {
            services.AddSingleton(log);
            services.AddSingleton<IBeforeUpdateHook<ContentDocument>, CaptureUpdate>();
        });
        var previous = new ContentDocument { ["title"] = "Old", ["seo"] = new Dictionary<string, object?> { ["metaTitle"] = "m" } };
        var entry = new ContentDocument { ["title"] = "New", ["seo"] = new Dictionary<string, object?> { ["metaTitle"] = "m2" } };

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeUpdate, entry, previous);

        var context = log.Contexts.ShouldHaveSingleItem();
        context.Operation.ShouldBeSameAs(HookFixtures.Operation);
        context.Entry.ShouldBeSameAs(entry);
        context.Previous.ShouldBeSameAs(previous);
        context.ChangedPaths.ShouldBe(["title", "seo.metaTitle"]);
        context.IsCancelled.ShouldBeFalse();
    }

    [Fact]
    public async Task ThereIsNoPreviousStateOnCreate()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(Create(log, typeof(Default)));

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc());

        var context = log.Contexts.ShouldHaveSingleItem();
        context.Previous.ShouldBeNull();
        context.ChangedPaths.ShouldBe(["title"]);
    }

    [Fact]
    public async Task EachKindRunsOnlyTheHooksOfThatKind()
    {
        foreach (var kind in Enum.GetValues<HookKind>())
        {
            var log = new Log();
            var pipeline = HookFixtures.Pipeline(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton<EveryKind>();
                services.AddSingleton<IBeforeValidateHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforeCreateHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterCreateHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterUpdateHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforeDeleteHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterDeleteHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforePublishHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterPublishHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforeUnpublishHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterUnpublishHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IBeforeRestoreHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
                services.AddSingleton<IAfterRestoreHook<ContentDocument>>(sp => sp.GetRequiredService<EveryKind>());
            });

            await HookFixtures.RunAsync(pipeline, kind, Doc());

            log.Entries.ShouldBe([kind.ToString()]);
        }
    }

    [Fact]
    public async Task HooksRunInHookOrderAndAHookWithoutTheAttributeHasOrderZero()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(Create(log, typeof(Late), typeof(Default), typeof(Early)));

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc());

        log.Entries.ShouldBe(["early", "default", "late"]);
    }

    [Fact]
    public async Task HooksOfEqualOrderRunInRegistrationOrderWithoutATiebreaker()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(Create(log, typeof(PluginB), typeof(PluginA)));

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc());

        log.Entries.ShouldBe(["B", "A"]);
    }

    [Fact]
    public async Task TheTiebreakerOrdersHooksOfEqualOrderOnly()
    {
        var log = new Log();
        var register = Create(log, typeof(PluginB), typeof(PluginA), typeof(Late), typeof(Early));
        var pipeline = HookFixtures.Pipeline(services =>
        {
            register(services);
            services.AddSingleton<IHookTiebreaker>(new RankByType(new() { [typeof(PluginA)] = -1, [typeof(PluginB)] = 1, [typeof(Late)] = -100 }));
        });

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc());

        log.Entries.ShouldBe(["early", "A", "B", "late"]);
    }

    [Fact]
    public async Task ACancellingHookThrowsWithItsCodeAndMessageAndLaterHooksDoNotRun()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(Create(log, typeof(Early), typeof(Rejecting), typeof(Late)));

        var exception = await Should.ThrowAsync<HookRejectedException>(HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc()).AsTask());

        exception.Code.ShouldBe("ACME0100");
        exception.Message.ShouldBe("Not today.");
        log.Entries.ShouldBe(["early", "rejecting"]);
    }

    [Fact]
    public async Task AHookCanRejectByThrowingTheException()
    {
        var pipeline = HookFixtures.Pipeline(Create(new Log(), typeof(Throwing)));

        var exception = await Should.ThrowAsync<HookRejectedException>(HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, Doc()).AsTask());

        exception.Code.ShouldBe("ACME0101");
    }

    [Fact]
    public async Task AnAfterHookCanCancelToo()
    {
        var pipeline = HookFixtures.Pipeline(services => services.AddSingleton<IAfterCreateHook<ContentDocument>, RejectingAfter>());

        var exception = await Should.ThrowAsync<HookRejectedException>(HookFixtures.RunAsync(pipeline, HookKind.AfterCreate, Doc()).AsTask());

        exception.Code.ShouldBe("ACME0102");
    }

    [Fact]
    public async Task AMutatingBeforeHookChangeReachesTheCallerAndTheNextHook()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(Create(log, typeof(Mutating), typeof(SeesTitle)));
        var entry = Doc("hello");

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, entry);

        entry.Get<string>("title").ShouldBe("HELLO");
        entry.Get<bool>("addedByHook").ShouldBeTrue();
        log.Entries.ShouldBe(["HELLO"]);
    }

    [Fact]
    public async Task ATypedHookGetsTheMappedEntryAndItsChangeReachesTheDocument()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(services =>
        {
            services.AddSingleton(log);
            services.AddSingleton<IHookEntryMapper, HookFixtures.ArticleMapper>();
            services.AddSingleton<IBeforeUpdateHook<Article>, TypedRetitle>();
            services.AddSingleton<IBeforeUpdateHook<ContentDocument>, AllTypes>();
        });
        var entry = Doc("New");

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeUpdate, entry, Doc("Old"));

        entry.Get<string>("title").ShouldBe("New (typed)");
        entry.Get<string>("slug").ShouldBe("set-by-typed-hook");
        log.Entries.ShouldBe(["doc:New", "typed:Old->New"]);
    }

    [Fact]
    public async Task AllTypesAndTypedHooksFormOneSequenceByOrderWithAllTypesFirstOnATie()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(services =>
        {
            services.AddSingleton(log);
            services.AddSingleton<IHookEntryMapper, HookFixtures.ArticleMapper>();
            services.AddSingleton<IBeforeUpdateHook<Article>, TypedRetitle>();
            services.AddSingleton<IBeforeUpdateHook<Article>, EarlyTyped>();
            services.AddSingleton<IBeforeUpdateHook<ContentItem>, BaseClassHook>();
            services.AddSingleton<IBeforeUpdateHook<ContentDocument>, AllTypes>();
        });

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeUpdate, Doc("T"), Doc("T"));

        log.Entries.ShouldBe(["typed-early", "doc:T", "base:Article", "typed:T->T"]);
    }

    [Fact]
    public async Task ATypedHookRunsOnlyForItsTypeAndAnAllTypesHookRunsForEveryType()
    {
        var log = new Log();
        var pipeline = HookFixtures.Pipeline(services =>
        {
            services.AddSingleton(log);
            services.AddSingleton<IHookEntryMapper, HookFixtures.ArticleMapper>();
            services.AddSingleton<IBeforeUpdateHook<Article>, EarlyTyped>();
            services.AddSingleton<IBeforeUpdateHook<ContentDocument>, AllTypes>();
        });
        var types = SampleSchema.FromClasses().Build().ContentTypes;

        types.Count.ShouldBeGreaterThan(2);
        foreach (var type in types)
        {
            await HookFixtures.RunAsync(pipeline, HookKind.BeforeUpdate, Doc(type.Name), type: type);
        }

        log.Entries.Count(entry => entry.StartsWith("doc:", StringComparison.Ordinal)).ShouldBe(types.Count);
        log.Entries.Count(entry => entry == "typed-early").ShouldBe(1);
    }

    [Fact]
    public async Task ATypedHookWithoutAMapperFails()
    {
        var pipeline = HookFixtures.Pipeline(services =>
        {
            services.AddSingleton(new Log());
            services.AddSingleton<IBeforeUpdateHook<Article>, EarlyTyped>();
        });

        await Should.ThrowAsync<InvalidOperationException>(HookFixtures.RunAsync(pipeline, HookKind.BeforeUpdate, Doc()).AsTask());
    }

    [Fact]
    public async Task WithoutHooksNothingHappens()
    {
        var pipeline = HookFixtures.Pipeline(_ => { });
        var entry = Doc();

        await HookFixtures.RunAsync(pipeline, HookKind.BeforeCreate, entry);

        entry.Count.ShouldBe(1);
    }
}
