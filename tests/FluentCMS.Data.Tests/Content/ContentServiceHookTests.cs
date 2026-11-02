using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Hooks;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceHookTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private sealed class Log
    {
        public List<string> Calls { get; } = [];

        public List<IReadOnlyList<string>> ChangedPaths { get; } = [];
    }

    private sealed class EveryHook(Log log, ServiceDatabase? database = null) :
        IBeforeValidateHook<ContentDocument>,
        IBeforeCreateHook<ContentDocument>,
        IAfterCreateHook<ContentDocument>,
        IBeforeUpdateHook<ContentDocument>,
        IAfterUpdateHook<ContentDocument>,
        IBeforeDeleteHook<ContentDocument>,
        IAfterDeleteHook<ContentDocument>,
        IBeforeRestoreHook<ContentDocument>,
        IAfterRestoreHook<ContentDocument>
    {
        private ValueTask Record(string name, HookContext<ContentDocument> context)
        {
            log.Calls.Add(name);
            log.ChangedPaths.Add(context.ChangedPaths);
            _ = database;
            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeValidateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("BeforeValidate", context);

        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("BeforeCreate", context);

        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("AfterCreate", context);

        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("BeforeUpdate", context);

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("AfterUpdate", context);

        public ValueTask BeforeDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("BeforeDelete", context);

        public ValueTask AfterDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("AfterDelete", context);

        public ValueTask BeforeRestoreAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("BeforeRestore", context);

        public ValueTask AfterRestoreAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Record("AfterRestore", context);
    }

    private static void RegisterEvery(IServiceCollection services, EveryHook hook)
    {
        services.AddSingleton<IBeforeValidateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeCreateHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterCreateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterUpdateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeDeleteHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterDeleteHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeRestoreHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterRestoreHook<ContentDocument>>(hook);
    }

    [Fact]
    public async Task HooksRunInTheDocumentedOrderWithTheChangedFields()
    {
        var log = new Log();
        using var database = new ServiceDatabase(cms => RegisterEvery(cms.Services, new EveryHook(log)));

        var entry = await database.CreateAsync(Editor, Titled("Hello"));
        log.Calls.ShouldBe(["BeforeValidate", "BeforeCreate", "AfterCreate"]);
        log.Calls.Clear();
        log.ChangedPaths.Clear();

        await database.PatchAsync(Editor, IdOf(entry), new() { ["subtitle"] = "Sub" }, 1);
        log.Calls.ShouldBe(["BeforeValidate", "BeforeUpdate", "AfterUpdate"]);
        log.ChangedPaths.ShouldAllBe(paths => paths.Count == 1 && paths[0] == "subtitle");
        log.Calls.Clear();

        await database.DeleteAsync(Editor, IdOf(entry));
        await database.RestoreAsync(Editor, IdOf(entry));
        log.Calls.ShouldBe(["BeforeDelete", "AfterDelete", "BeforeRestore", "AfterRestore"]);
    }

    private sealed class Cancel(string kind) :
        IBeforeValidateHook<ContentDocument>,
        IBeforeCreateHook<ContentDocument>,
        IAfterCreateHook<ContentDocument>,
        IBeforeUpdateHook<ContentDocument>,
        IAfterUpdateHook<ContentDocument>,
        IBeforeDeleteHook<ContentDocument>,
        IAfterDeleteHook<ContentDocument>
    {
        private ValueTask Maybe(string name, HookContext<ContentDocument> context)
        {
            if (name == kind)
            {
                context.Cancel("ACME0001", "No.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeValidateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeValidate", context);

        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeCreate", context);

        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("AfterCreate", context);

        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeUpdate", context);

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("AfterUpdate", context);

        public ValueTask BeforeDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeDelete", context);

        public ValueTask AfterDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("AfterDelete", context);
    }

    private static void RegisterCancel(IServiceCollection services, string kind)
    {
        var hook = new Cancel(kind);
        services.AddSingleton<IBeforeValidateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeCreateHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterCreateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterUpdateHook<ContentDocument>>(hook);
        services.AddSingleton<IBeforeDeleteHook<ContentDocument>>(hook);
        services.AddSingleton<IAfterDeleteHook<ContentDocument>>(hook);
    }

    [Theory]
    [InlineData("BeforeValidate")]
    [InlineData("BeforeCreate")]
    [InlineData("AfterCreate")]
    public async Task ACancellingHookOnCreateRollsBackEveryTable(string kind)
    {
        using var database = new ServiceDatabase(cms => RegisterCancel(cms.Services, kind));
        var author = Guid.CreateVersion7();
        var before = database.Footprint();

        var refused = await Should.ThrowAsync<HookRejectedException>(() => database.CreateAsync(Editor, new() { ["title"] = "Hello", ["author"] = author }));

        refused.Code.ShouldBe("ACME0001");
        database.Footprint().ShouldBe(before);
    }

    [Theory]
    [InlineData("BeforeValidate")]
    [InlineData("BeforeUpdate")]
    [InlineData("AfterUpdate")]
    public async Task ACancellingHookOnUpdateRollsBackEveryTable(string kind)
    {
        var active = new ActiveKind();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeValidateHook<ContentDocument>>(new SwitchCancel(active)).AddSingleton<IBeforeUpdateHook<ContentDocument>>(new SwitchCancel(active)).AddSingleton<IAfterUpdateHook<ContentDocument>>(new SwitchCancel(active)));
        var author = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var entry = await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1, ["author"] = author });
        var before = database.Footprint();
        active.Kind = kind;

        await Should.ThrowAsync<HookRejectedException>(() => database.PatchAsync(Editor, IdOf(entry), new() { ["title"] = "Changed", ["views"] = 2, ["author"] = null }, 1));

        active.Kind = "";
        database.Footprint().ShouldBe(before);
        var stored = (await database.GetAsync(Editor, IdOf(entry)))!;
        stored["title"].ShouldBe("Hello");
        stored["views"].ShouldBe(1L);
        stored["version"].ShouldBe(1);
        stored["author"].ShouldNotBeNull();
    }

    private sealed class ActiveKind
    {
        public string Kind { get; set; } = "";
    }

    private sealed class SwitchCancel(ActiveKind active) :
        IBeforeValidateHook<ContentDocument>,
        IBeforeUpdateHook<ContentDocument>,
        IAfterUpdateHook<ContentDocument>
    {
        private ValueTask Maybe(string name, HookContext<ContentDocument> context)
        {
            if (active.Kind == name)
            {
                context.Cancel("ACME0001", "No.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeValidateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeValidate", context);

        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("BeforeUpdate", context);

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Maybe("AfterUpdate", context);
    }

    private sealed class ChangeInBeforeCreate(string field, object? value) : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry[field] = value;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AHooksChangeIsStoredInCanonicalFormAndChecked()
    {
        using var stored = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("publishedOn", "2026-11-02T10:00:00+02:00")));
        var entry = await stored.CreateAsync(Editor, Titled("Hello"));
        ((DateTimeOffset)entry["publishedOn"]!).ShouldBe(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

        using var invalid = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("views", 5000)));
        var before = invalid.Footprint();
        (await Should.ThrowAsync<ValidationException>(() => invalid.CreateAsync(Editor, Titled("Hello"))))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("views"),
                error => error.Code.ShouldBe(ValidationErrorCodes.AboveMaximum));
        invalid.Footprint().ShouldBe(before);

        using var unknown = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("nothing", 1)));
        (await Should.ThrowAsync<ValidationException>(() => unknown.CreateAsync(Editor, Titled("Hello"))))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("nothing");
    }

    [Fact]
    public async Task UniquenessChecksWhatABeforeHookLeft()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("code", "same")));
        await database.CreateAsync(Editor, Titled("One"));

        (await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Titled("Two"))))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("code");
    }

    [Fact]
    public async Task AHookCannotWriteAFieldThePrincipalMayNot()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("views", 5)));
        var role = database.Database.AddRoleForTest("Limited", ("content.article.create", new PermissionConstraints { Fields = ["title"] }), ("content.article.read", null));
        var before = database.Footprint();

        (await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(WithRole(role), Titled("Hello"))))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("views"),
                error => error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable));
        database.Footprint().ShouldBe(before);

        // An editor may write it, so the hook may too.
        await Should.NotThrowAsync(() => database.CreateAsync(Editor, Titled("Other")));
    }

    [Fact]
    public async Task AHookFillsAHiddenFieldThatTheResponseLeavesOut()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new ChangeInBeforeCreate("secret", "computed")));
        var role = database.Database.AddRoleForTest("Limited", ("content.article.create", new PermissionConstraints { Fields = ["title"] }), ("content.article.read", null));

        var entry = await database.CreateAsync(WithRole(role), Titled("Hello"));

        entry.ContainsKey("secret").ShouldBeFalse();
        database.Row("cms_article", IdOf(entry))["secret"].ShouldBe("computed");
    }

    private sealed class ChangeSystemFields : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["createdBy"] = Guid.CreateVersion7();
            context.Entry["version"] = 77;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AHookCannotSetASystemField()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(new ChangeSystemFields()));
        var entry = await database.CreateAsync(Editor, Titled("Hello"));

        var updated = await database.PatchAsync(Editor, IdOf(entry), Titled("Changed"), 1);

        updated["version"].ShouldBe(2);
        database.Read(context => context.Documents.Single(document => document.DocumentId == IdOf(entry)).CreatedBy).ShouldBe(Editor.Id);
    }

    private sealed class Rename : IBeforeUpdateHook<Article>
    {
        public ValueTask BeforeUpdateAsync(HookContext<Article> context, CancellationToken cancellationToken)
        {
            context.Entry.Title = context.Previous!.Title + " and more";
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ATypedHookSeesTheClassAndChangesOnlyWhatItChanges()
    {
        var schema = SampleSchema.FromClasses().Build();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<Article>, Rename>(), schema);
        var entry = await database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["category"] = "opinion",
            ["sections"] = new List<ContentDocument> { new() { ["type"] = "quote", ["text"] = "Hi" } },
        });

        var role = database.Database.AddRoleForTest("Limited", ("content.article.update", new PermissionConstraints { Fields = ["title", "slug"] }), ("content.article.read", null));

        var updated = await database.PatchAsync(WithRole(role), IdOf(entry), new() { ["slug"] = "other" }, 1);

        updated["title"].ShouldBe("Hello and more");
        updated["category"].ShouldBe("opinion");
        updated["slug"].ShouldBe("other");
    }
}
