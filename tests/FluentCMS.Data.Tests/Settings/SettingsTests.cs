using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Events;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Settings.SettingsDatabase;

namespace FluentCMS.Data.Tests.Settings;

public sealed class SettingsTests
{
    private const string Secret = "hunter2-secret-value";

    [Fact]
    public async Task ASettingsClassIsReadableAndWritablePerSite()
    {
        using var database = new SettingsDatabase();

        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(25);

        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 80, Provider = "s3", Tags = ["a", "b"], Quality = Quality.Low });

        var site = await database.GetAsync(SiteA);
        site.MaxUploadMb.ShouldBe(80);
        site.Provider.ShouldBe("s3");
        site.Tags.ShouldBe(["a", "b"]);
        site.Quality.ShouldBe(Quality.Low);
        (await database.GetAsync(SiteB)).MaxUploadMb.ShouldBe(25);

        var row = database.Rows().ShouldHaveSingleItem();
        row.SiteId.ShouldBe(SiteA.Value);
        row.Key.ShouldBe("media");
        row.Version.ShouldBe(1);
        row.UpdatedBy.ShouldBe(Admin.Id);
        row.UpdatedAt.ShouldBe(database.Clock.GetUtcNow());
        JsonNode.Parse(row.Json)!["quality"]!.GetValue<string>().ShouldBe("low");
    }

    [Fact]
    public async Task ASiteOverrideWinsOverTheInstanceDefaultWhichWinsOverTheCodeDefault()
    {
        using var database = new SettingsDatabase();

        (await database.GetAsync(SiteA)).Provider.ShouldBe("local");

        await database.SetDefaultAsync(new MediaSettings { Provider = "azure" });
        (await database.GetAsync(SiteA)).Provider.ShouldBe("azure");
        (await database.GetAsync(SiteB)).Provider.ShouldBe("azure");
        (await database.GetDefaultAsync()).Provider.ShouldBe("azure");

        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" });
        (await database.GetAsync(SiteA)).Provider.ShouldBe("s3");
        (await database.GetAsync(SiteB)).Provider.ShouldBe("azure");

        await database.AsAsync(Admin, async media =>
        {
            await media.ResetAsync(SiteA, Token);
            return true;
        });
        (await database.GetAsync(SiteA)).Provider.ShouldBe("azure");

        await database.AsAsync(Admin, async media =>
        {
            await media.ResetDefaultAsync(Token);
            return true;
        });
        (await database.GetAsync(SiteA)).Provider.ShouldBe("local");
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task AReadGivesACopyThatTheCallerMayChange()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { Tags = ["x"] });

        (await database.GetAsync(SiteA)).Tags.Add("changed");

        (await database.GetAsync(SiteA)).Tags.ShouldBe(["x"]);
    }

    [Fact]
    public async Task ChangingASettingRaisesTheEventAndWritesAnActivityRowForTheSite()
    {
        using var database = new SettingsDatabase();

        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" });
        await database.Inner.DrainAsync();

        var heard = database.Seen.Of<SettingsChangedEvent>().ShouldHaveSingleItem();
        heard.Area.ShouldBe("media");
        heard.Source.SiteId.ShouldBe(SiteA.Value);
        heard.Source.ActorId.ShouldBe(Admin.Id);

        var row = database.Inner.ActivityRows().ShouldHaveSingleItem();
        row.Action.ShouldBe("settings.changed");
        row.TargetType.ShouldBe("settings");
        row.TargetId.ShouldBe("media");
        row.SiteId.ShouldBe(SiteA.Value);
        row.ActorId.ShouldBe(Admin.Id);
    }

    [Fact]
    public async Task ChangingTheInstanceDefaultIsAnEventAndARowOfNoSiteEvenWhenTheOperationHasOne()
    {
        using var database = new SettingsDatabase();

        await database.AsAsync(Admin, async media =>
        {
            await media.SetDefaultAsync(new MediaSettings { Provider = "s3" }, Token);
            return true;
        }, SiteB);
        await database.Inner.DrainAsync();

        database.Seen.Of<SettingsChangedEvent>().ShouldHaveSingleItem().Source.SiteId.ShouldBe(Guid.Empty);
        database.Inner.ActivityRows().ShouldHaveSingleItem().SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task ResettingAnAreaThatHasNoRowRaisesNothing()
    {
        using var database = new SettingsDatabase();

        await database.AsAsync(Admin, async media =>
        {
            await media.ResetAsync(SiteA, Token);
            await media.ResetDefaultAsync(Token);
            return true;
        });

        database.Seen.Of<SettingsChangedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWriteDropsWhatReadersKeepAcrossScopes()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 10 });
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(10);

        // A change behind the service's back is not seen while the read is kept, which shows that it is kept.
        database.EditRow(SiteA.Value, "media", """{"maxUploadMb":11,"provider":"local","quality":"low","tags":[]}""");
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(10);

        // A write from a scope of its own, then a read from another, and the same for the instance default.
        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 12 });
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(12);

        await database.SetDefaultAsync(new MediaSettings { MaxUploadMb = 20 });
        (await database.GetAsync(SiteB)).MaxUploadMb.ShouldBe(20);
        await database.SetDefaultAsync(new MediaSettings { MaxUploadMb = 30 });
        (await database.GetAsync(SiteB)).MaxUploadMb.ShouldBe(30);
        (await database.GetDefaultAsync()).MaxUploadMb.ShouldBe(30);

        // Another area is not disturbed by it.
        var title = await database.AsAsync(Admin, async services =>
        {
            var seo = services.GetRequiredService<ISettings<SeoSettings>>();
            await seo.SetAsync(SiteA, new SeoSettings { Title = "T" }, Token);
            return (await seo.GetAsync(SiteA, Token)).Title;
        });
        title.ShouldBe("T");
    }

    [Fact]
    public async Task AnotherInstanceSeesAWriteWhenItsReadsExpire()
    {
        using var database = new SettingsDatabase();
        var clock = database.Clock;
        using var other = database.Inner.Database.NewInstance(cms =>
        {
            cms.Settings.Add<MediaSettings>().Add<SeoSettings>();
            cms.Services.AddSingleton<TimeProvider>(clock);
        });

        async Task<int> OtherReadsAsync()
        {
            using var scope = other.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<ISettings<MediaSettings>>().GetAsync(SiteA, Token)).MaxUploadMb;
        }

        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 10 });
        (await OtherReadsAsync()).ShouldBe(10);

        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 40 });

        (await OtherReadsAsync()).ShouldBe(10);
        clock.Advance(TimeSpan.FromSeconds(29));
        (await OtherReadsAsync()).ShouldBe(10);
        clock.Advance(TimeSpan.FromSeconds(2));
        (await OtherReadsAsync()).ShouldBe(40);
    }

    [Fact]
    public async Task ACacheDurationOfZeroReadsTheDatabaseEveryTime()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.CacheDuration = TimeSpan.Zero);
        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 10 });
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(10);

        database.EditRow(SiteA.Value, "media", """{"maxUploadMb":11,"provider":"local","quality":"low","tags":[]}""");

        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(11);
    }

    [Fact]
    public async Task AWriteWithoutThePermissionChangesNothingAndTellsNoOne()
    {
        using var database = new SettingsDatabase();
        var nobody = UserWith(BuiltInRoles.Editor);

        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteA, new MediaSettings(), nobody));
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetDefaultAsync(new MediaSettings(), nobody));
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.AsAsync(nobody, async media =>
        {
            await media.ResetAsync(SiteA, Token);
            return true;
        }));

        database.Rows().ShouldBeEmpty();
        database.Seen.Events.ShouldBeEmpty();
        database.Inner.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASiteGrantChangesThatSiteAndNeverTheInstanceDefaultOrAnotherSite()
    {
        using var database = new SettingsDatabase();
        var role = database.Inner.Database.AddRoleForTest("media-a", SiteA.Value, ("settings.media.manage", null));
        var manager = UserWith(role);

        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" }, manager);

        var other = await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteB, new MediaSettings(), manager));
        other.Site.ShouldBe(SiteB);
        var instance = await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetDefaultAsync(new MediaSettings(), manager));
        instance.Site.ShouldBeNull();
        database.Rows().ShouldHaveSingleItem().SiteId.ShouldBe(SiteA.Value);
    }

    [Fact]
    public async Task AGrantLimitedToSitesOfARoleWithNoSiteIsNotAnInstanceGrant()
    {
        using var database = new SettingsDatabase();
        var limited = database.Inner.Database.AddRoleForTest("media-limited", ("settings.media.manage", new PermissionConstraints { Sites = [SiteA] }));
        var unlimited = database.Inner.Database.AddRoleForTest("media-all", ("settings.media.manage", null));

        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" }, UserWith(limited));
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteB, new MediaSettings(), UserWith(limited)));
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetDefaultAsync(new MediaSettings(), UserWith(limited)));

        await database.SetDefaultAsync(new MediaSettings { Provider = "azure" }, UserWith(unlimited));
        await database.SetAsync(SiteB, new MediaSettings { Provider = "gcs" }, UserWith(unlimited));
        (await database.GetAsync(SiteB)).Provider.ShouldBe("gcs");
        (await database.GetDefaultAsync()).Provider.ShouldBe("azure");
    }

    [Fact]
    public async Task ThePermissionIsPerAreaAndThePrincipalMustHaveTheManagementScope()
    {
        using var database = new SettingsDatabase();
        var seoOnly = database.Inner.Database.AddRoleForTest("seo", ("settings.seo.manage", null));
        var media = database.Inner.Database.AddRoleForTest("media", ("settings.media.manage", null));

        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteA, new MediaSettings(), UserWith(seoOnly)));

        var delivery = AuthorizationDatabase.User([media], [TokenScopes.Delivery]);
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteA, new MediaSettings(), delivery));
        var anonymous = Principal.Anonymous;
        await Should.ThrowAsync<SettingsAccessDeniedException>(() => database.SetAsync(SiteA, new MediaSettings(), anonymous));

        await database.SetAsync(SiteA, new MediaSettings(), UserWith(media));
    }

    [Fact]
    public async Task TheSystemPrincipalMayWriteAndNoOperationAtAllMayNot()
    {
        using var database = new SettingsDatabase();

        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" }, Principal.System);
        (await database.GetAsync(SiteA)).Provider.ShouldBe("s3");

        using var scope = database.Inner.Database.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<ISettings<MediaSettings>>();
        await Should.ThrowAsync<InvalidOperationException>(async () => await media.SetAsync(SiteA, new MediaSettings(), Token));
    }

    [Fact]
    public async Task AWriteInsideATransactionIsRefusedBeforeItChangesAnything()
    {
        using var database = new SettingsDatabase();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => database.AsAsync(Admin, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await services.GetRequiredService<ISettings<MediaSettings>>().SetAsync(SiteA, new MediaSettings(), Token);
            return true;
        }));

        exception.Message.ShouldContain("FCMS2807");
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task ADocumentWithAnUnknownKeyOrInvalidJsonIsRefusedWithValidationErrors()
    {
        using var database = new SettingsDatabase();

        async Task<ValidationError[]> RefusedAsync(string json) =>
            (await Should.ThrowAsync<ValidationException>(() => database.ServiceAsync(async (service, descriptor) =>
            {
                await service.WriteAsync(descriptor, SiteA, json, null, Token);
                return true;
            }))).Errors.ToArray();

        var unknown = await RefusedAsync("""{"maxUploadMb":10,"nope":1,"alsoNot":2}""");
        unknown.Select(error => (error.Path, error.Code)).ShouldBe([("nope", "FCMS2804"), ("alsoNot", "FCMS2804")]);

        (await RefusedAsync("{")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2803");
        (await RefusedAsync("[]")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2803");
        (await RefusedAsync("null")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2803");
        (await RefusedAsync("")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2803");
        (await RefusedAsync("""{"maxUploadMb":1,"maxUploadMb":2}""")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2805");
        (await RefusedAsync("""{"maxUploadMb":"many"}""")).ShouldHaveSingleItem().Path.ShouldBe("maxUploadMb");
        (await RefusedAsync("""{"quality":"best"}""")).ShouldHaveSingleItem().Path.ShouldBe("quality");
        (await RefusedAsync("""{"provider":null}""")).ShouldHaveSingleItem().Path.ShouldBe("provider");
        (await RefusedAsync("{\"tags\":[" + new string(' ', 70_000) + "]}")).ShouldHaveSingleItem().Code.ShouldBe("FCMS2803");

        database.Rows().ShouldBeEmpty();
        database.Seen.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AValueThatBreaksAValidationAttributeIsRefusedForTheTypedWriteToo()
    {
        using var database = new SettingsDatabase();

        var errors = (await Should.ThrowAsync<ValidationException>(() => database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 9000, Provider = "NOT lower" }))).Errors;

        errors.Select(error => error.Path).ShouldBe(["maxUploadMb", "provider"], ignoreOrder: true);
        errors.ShouldAllBe(error => error.Code == "FCMS2805");
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAreaIsRefused()
    {
        using var database = new SettingsDatabase();

        var exception = await Should.ThrowAsync<ValidationException>(() => database.AsAsync(Admin, services =>
        {
            services.GetRequiredService<SettingsService>().Require("nope");
            return Task.FromResult(true);
        }));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2802");
    }

    [Fact]
    public async Task ASettingsClassThatTheHostDidNotAddCannotBeResolved()
    {
        using var database = new SettingsDatabase();

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => database.AsAsync(Admin, services =>
        {
            services.GetRequiredService<ISettings<NoAttribute>>();
            return Task.FromResult(true);
        }));

        exception.Code.ShouldBe("FCMS2801");
    }

    [Fact]
    public void ABadRegistrationStopsTheHost()
    {
        static FluentCmsConfigurationException Refused(params Type[] types) =>
            Should.Throw<FluentCmsConfigurationException>(() => new SettingsRegistry(types, TimeSpan.Zero));

        Refused(typeof(NoAttribute)).Message.ShouldContain("[Settings");
        Refused(typeof(BadArea)).Message.ShouldContain("Bad Area");
        Refused(typeof(MediaSettings), typeof(SecondMedia)).Message.ShouldContain("both use");
        Refused(typeof(MediaSettings), typeof(MediaSettings)).Message.ShouldContain("twice");
        Refused(typeof(Empty)).Message.ShouldContain("read-write");
        Should.Throw<FluentCmsConfigurationException>(() => new SettingsBuilder { CacheDuration = TimeSpan.FromSeconds(-1) }.Build());
    }

    [Fact]
    public async Task ConcurrentWritesNeitherFailNorLoseARowAndEachRaisesItsEvent()
    {
        using var database = new SettingsDatabase();
        const int Writers = 12;

        await Task.WhenAll(Enumerable.Range(1, Writers).Select(number => Task.Run(() => database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = number }))));

        var row = database.Rows().ShouldHaveSingleItem();
        row.Version.ShouldBe(Writers);
        var stored = (await database.GetAsync(SiteA)).MaxUploadMb;
        stored.ShouldBeInRange(1, Writers);
        database.Seen.Of<SettingsChangedEvent>().Count().ShouldBe(Writers);

        // The document is one writer's whole document and not a mix: the last write stands.
        JsonNode.Parse(row.Json)!["maxUploadMb"]!.GetValue<int>().ShouldBe(stored);
    }

    [Fact]
    public async Task AWriterThatNamesTheVersionItReadLosesToAnEarlierWriter()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 10 });

        async Task WriteAsync(int megabytes, int? expected) =>
            await database.ServiceAsync(async (service, descriptor) =>
            {
                await service.WriteAsync(descriptor, SiteA, $$"""{"maxUploadMb":{{megabytes}}}""", expected, Token);
                return true;
            });

        var read = (await database.ServiceAsync((service, descriptor) => service.ResolveAsync(descriptor, SiteA, Token))).Version;
        read.ShouldBe(1);

        await WriteAsync(20, read);
        var conflict = await Should.ThrowAsync<ValidationException>(() => WriteAsync(30, read));
        conflict.Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2808");
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(20);

        // Version 0 says there was no row.
        await Should.ThrowAsync<ValidationException>(() => WriteAsync(40, 0));
        await WriteAsync(50, null);
        database.Rows().ShouldHaveSingleItem().Version.ShouldBe(3);
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(50);
    }

    [Fact]
    public async Task OnlyOneOfTwoFirstWritersThatNameVersionZeroWins()
    {
        using var database = new SettingsDatabase();

        var results = await Task.WhenAll(Enumerable.Range(1, 6).Select(number => Task.Run(async () =>
        {
            try
            {
                await database.ServiceAsync(async (service, descriptor) =>
                {
                    await service.WriteAsync(descriptor, SiteA, $$"""{"maxUploadMb":{{number}}}""", 0, Token);
                    return true;
                });
                return true;
            }
            catch (ValidationException exception) when (exception.Errors[0].Code == "FCMS2808")
            {
                return false;
            }
        })));

        results.Count(won => won).ShouldBe(1);
        database.Rows().ShouldHaveSingleItem().Version.ShouldBe(1);
    }

    [Fact]
    public async Task ASiteThatDoesNotExistIsRefusedOnReadAndWrite()
    {
        using var database = new SettingsDatabase();
        var unknown = new SiteId(Guid.CreateVersion7());

        (await Should.ThrowAsync<ValidationException>(() => database.GetAsync(unknown))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2806");
        (await Should.ThrowAsync<ValidationException>(() => database.GetAsync(SiteId.Empty))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2806");
        (await Should.ThrowAsync<ValidationException>(() => database.SetAsync(unknown, new MediaSettings()))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2806");
        (await Should.ThrowAsync<ValidationException>(() => database.AsAsync(Admin, async media =>
        {
            await media.ResetAsync(unknown, Token);
            return true;
        }))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2806");

        database.Rows().ShouldBeEmpty();
        database.Seen.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletingASiteRemovesItsSettingsAndWhatReadersKeptOfThem()
    {
        using var database = new SettingsDatabase();
        var doomed = new SiteId(Guid.CreateVersion7());
        await database.Inner.AsAsync(Admin, async services =>
        {
            var site = await services.GetRequiredService<FluentCMS.Core.Sites.SiteManager>().CreateAsync("Doomed", "doomed", "en", Token);
            doomed = new SiteId(site.Id);
            return true;
        });

        await database.SetAsync(doomed, new MediaSettings { Provider = "s3" });
        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3" });
        (await database.GetAsync(doomed)).Provider.ShouldBe("s3");

        await database.Inner.AsAsync(Admin, async services =>
        {
            await services.GetRequiredService<FluentCMS.Core.Sites.SiteManager>().DeleteAsync(doomed.Value, Token);
            return true;
        });

        database.Rows().ShouldHaveSingleItem().SiteId.ShouldBe(SiteA.Value);
        (await Should.ThrowAsync<ValidationException>(() => database.GetAsync(doomed))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2806");
        (await database.GetAsync(SiteA)).Provider.ShouldBe("s3");
    }

    [Fact]
    public async Task AStoredDocumentTheClassCannotReadFallsBackAndAnOldKeyDoesNotBreakTheRead()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { Provider = "azure" });
        database.AddRow(SiteA.Value, "media", """{"maxUploadMb":"not a number"}""");
        database.AddRow(SiteB.Value, "media", """{"maxUploadMb":7,"keyThatWasRemoved":true}""");

        (await database.GetAsync(SiteA)).Provider.ShouldBe("azure");
        var site = await database.GetAsync(SiteB);
        site.MaxUploadMb.ShouldBe(7);
        site.Provider.ShouldBe("local");
        database.Logs.All.ShouldContain("cannot be read");
    }

    [Fact]
    public async Task EveryAreaHasAPermissionInTheCatalogue()
    {
        using var database = new SettingsDatabase();
        var catalogue = database.Inner.Database.Services.GetRequiredService<PermissionCatalogue>();

        foreach (var area in new[] { "media", "seo" })
        {
            var entry = catalogue.Find($"settings.{area}.manage").ShouldNotBeNull();
            entry.Source.ShouldBe(PermissionSource.Settings);
            catalogue.IsGrantable($"settings.{area}.manage").ShouldBeTrue();
        }

        catalogue.IsGrantable("settings.*.manage").ShouldBeTrue();
        catalogue.Contains("settings.nope.manage").ShouldBeFalse();
    }

    [Fact]
    public async Task AWrittenSecretIsInTheTableAndInNoEventActivityRowOrLogLine()
    {
        using var database = new SettingsDatabase();

        await database.SetAsync(SiteA, new MediaSettings { StorageAccount = Secret + "-account", ApiKey = Secret + "-key", Passphrase = "pass" });
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-default" });
        await Should.ThrowAsync<ValidationException>(() => database.SetAsync(SiteB, new MediaSettings { Passphrase = Secret }));
        await Should.ThrowAsync<ValidationException>(() => database.ServiceAsync(async (service, descriptor) =>
        {
            await service.WriteAsync(descriptor, SiteB, $$"""{"passphrase":"{{Secret}}","bogus":"{{Secret}}"}""", null, Token);
            return true;
        }));

        database.Rows().Select(row => row.Json).ShouldContain(json => json.Contains(Secret + "-key", StringComparison.Ordinal));
        database.Seen.Events.Count.ShouldBe(2);

        var traces = await database.TracesAsync();
        traces.ShouldNotContain(Secret);
        traces.ShouldNotContain("default-account-8841");
        traces.ShouldContain("settings.changed");
    }

    [Fact]
    public async Task AValidationErrorOfASecretNeverRepeatsItsValue()
    {
        using var database = new SettingsDatabase();

        var errors = (await Should.ThrowAsync<ValidationException>(() => database.SetAsync(SiteA, new MediaSettings { Passphrase = Secret }))).Errors;

        var error = errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("passphrase");
        error.Message.ShouldNotContain(Secret);
        error.Message.ShouldNotContain("Passphrase");
    }

    [Fact]
    public async Task TheSchemaComesFromTheTypeAndItsAttributesAndShowsNoSecret()
    {
        using var database = new SettingsDatabase();
        var schema = (await database.ServiceAsync((_, descriptor) => Task.FromResult(descriptor.CreateSchema()))).AsObject();

        schema["title"]!.GetValue<string>().ShouldBe("media");
        schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
        var properties = schema["properties"]!.AsObject();
        properties.Select(property => property.Key).ShouldBe(["maxUploadMb", "provider", "quality", "tags", "storageAccount", "apiKey", "passphrase"], ignoreOrder: true);

        var size = properties["maxUploadMb"]!.AsObject();
        size["description"]!.GetValue<string>().ShouldBe("The largest upload, in megabytes.");
        size["minimum"]!.GetValue<int>().ShouldBe(1);
        size["maximum"]!.GetValue<int>().ShouldBe(500);
        size["default"]!.GetValue<int>().ShouldBe(25);

        var provider = properties["provider"]!.AsObject();
        provider["maxLength"]!.GetValue<int>().ShouldBe(20);
        provider["pattern"]!.GetValue<string>().ShouldBe("^[a-z0-9]+$");
        provider["default"]!.GetValue<string>().ShouldBe("local");
        schema["required"]!.AsArray().Select(name => name!.GetValue<string>()).ShouldBe(["provider"]);

        var quality = properties["quality"]!.AsObject();
        quality["enum"]!.AsArray().Select(name => name!.GetValue<string>()).ShouldBe(["low", "highQuality"]);
        quality["default"]!.GetValue<string>().ShouldBe("highQuality");

        foreach (var secret in new[] { "storageAccount", "apiKey", "passphrase" })
        {
            var property = properties[secret]!.AsObject();
            property["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
            property.ContainsKey("default").ShouldBeFalse();
        }

        schema.ToJsonString().ShouldNotContain("default-account-8841");
        properties["passphrase"]!["maxLength"]!.GetValue<int>().ShouldBe(8);
    }

    private static Task<bool> WriteDocumentAsync(SettingsDatabase database, string area, SiteId? site, string json, int? expected = null) =>
        database.AsAsync(Admin, async services =>
        {
            var service = services.GetRequiredService<SettingsService>();
            await service.WriteAsync(service.Require(area), site, json, expected, Token);
            return true;
        });

    [Fact]
    public async Task AValidationAttributeBelowTheTopLevelIsEnforcedWithAPathForTheKey()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<NestedSettings>());

        async Task<ValidationError> RefusedAsync(string json) =>
            (await Should.ThrowAsync<ValidationException>(() => WriteDocumentAsync(database, "nested", SiteA, json))).Errors.ShouldHaveSingleItem();

        (await RefusedAsync("""{"limits":{"port":99}}""")).ShouldBe(new ValidationError("limits.port", "FCMS2805", "The field Port must be between 1 and 10."));
        (await RefusedAsync("""{"many":[{"port":2},{"port":99}]}""")).Path.ShouldBe("many[1].port");
        (await RefusedAsync("""{"byName":{"a":{"port":0}}}""")).Path.ShouldBe("byName.a.port");

        var typed = await Should.ThrowAsync<ValidationException>(() => database.AsAsync(Admin, async services =>
        {
            await services.GetRequiredService<ISettings<NestedSettings>>().SetAsync(SiteA, new NestedSettings { Limits = new Limits { Port = 99 } }, Token);
            return true;
        }));
        typed.Errors.ShouldHaveSingleItem().Path.ShouldBe("limits.port");

        await WriteDocumentAsync(database, "nested", SiteA, """{"limits":{"port":10},"many":[{"port":1}]}""");
        database.Rows().ShouldHaveSingleItem();
    }

    [Fact]
    public void ASecretBelowTheTopLevelStopsTheHost()
    {
        foreach (var type in new[] { typeof(SecretInAnObject), typeof(SecretInAList) })
        {
            var exception = Should.Throw<FluentCmsConfigurationException>(() => new SettingsRegistry([type], TimeSpan.Zero));
            exception.Code.ShouldBe("FCMS2801");
            exception.Message.ShouldContain("Password");
        }
    }

    [Fact]
    public void ANestedPropertyNamedLikeASecretStopsTheHostToo()
    {
        foreach (var type in new[] { typeof(NamedSecretInAnObject), typeof(NamedSecretInAList) })
        {
            var exception = Should.Throw<FluentCmsConfigurationException>(() => new SettingsRegistry([type], TimeSpan.Zero));
            exception.Code.ShouldBe("FCMS2801");
            exception.Message.ShouldContain("Password");
            exception.Message.ShouldContain("top level");
        }
    }

    [Fact]
    public void OnlyTopLevelPropertiesAreSecretInTheSchema()
    {
        var descriptor = new SettingsRegistry([typeof(NestedSettings)], TimeSpan.Zero).Find("nested")!;
        descriptor.SecretKeys.ShouldBe(["token"]);

        var schema = descriptor.CreateSchema().AsObject();
        var properties = schema["properties"]!.AsObject();

        properties["token"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
        properties["token"]!.AsObject().ContainsKey("default").ShouldBeFalse();
        schema.ToJsonString().ShouldNotContain("root-token-default-31");

        properties["smtp"]!["properties"]!["sender"]!.AsObject().ContainsKey("writeOnly").ShouldBeFalse();
        properties["smtp"]!["default"]!["sender"]!.GetValue<string>().ShouldBe("nested-sender-default-52");
    }

    [Fact]
    public async Task AResetThatNamesVersionZeroNeverDeletesARowAndAStaleVersionConflicts()
    {
        using var database = new SettingsDatabase();

        Task<bool> ResetAsync(int? expected) => database.ServiceAsync(async (service, descriptor) =>
        {
            await service.ResetAsync(descriptor, SiteA, expected, Token);
            return true;
        });

        await ResetAsync(0);
        database.Seen.Events.ShouldBeEmpty();

        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 10 });
        await database.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 11 });
        database.Seen.Events.Count.ShouldBe(2);

        (await Should.ThrowAsync<ValidationException>(() => ResetAsync(0))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2808");
        (await Should.ThrowAsync<ValidationException>(() => ResetAsync(1))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2808");
        database.Rows().ShouldHaveSingleItem().Version.ShouldBe(2);

        await ResetAsync(2);
        database.Rows().ShouldBeEmpty();
        database.Seen.Events.Count.ShouldBe(3);
        (await Should.ThrowAsync<ValidationException>(() => ResetAsync(2))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2808");
    }

    [Fact]
    public async Task AWriteNeitherSavesNorDropsWhatTheCallerHasTracked()
    {
        using var database = new SettingsDatabase();

        var state = await database.AsAsync(Admin, async services =>
        {
            var system = services.GetRequiredService<SystemDbContext>();
            var site = await system.Set<Site>().SingleAsync(item => item.Id == SiteA.Value, Token);
            site.Name = "renamed-by-accident";

            var media = services.GetRequiredService<ISettings<MediaSettings>>();
            await media.SetAsync(SiteA, new MediaSettings(), Token);
            await media.SetAsync(SiteA, new MediaSettings { MaxUploadMb = 3 }, Token);
            await media.ResetAsync(SiteA, Token);

            return system.Entry(site).State;
        });

        state.ShouldBe(EntityState.Modified);
        database.Inner.Read(context => context.Set<Site>().AsNoTracking().Single(item => item.Id == SiteA.Value).Name).ShouldNotBe("renamed-by-accident");
    }

    [Fact]
    public async Task ASecretLeftOutOfAWholeDocumentWriteKeepsItsValueAndOneThatIsSentReplacesIt()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret + "-key", StorageAccount = "kept-account", Passphrase = "pass" });

        await WriteDocumentAsync(database, "media", SiteA, """{"maxUploadMb":30}""");

        var kept = await database.GetAsync(SiteA);
        kept.MaxUploadMb.ShouldBe(30);
        kept.ApiKey.ShouldBe(Secret + "-key");
        kept.StorageAccount.ShouldBe("kept-account");
        kept.Passphrase.ShouldBe("pass");

        await WriteDocumentAsync(database, "media", SiteA, """{"apiKey":null,"passphrase":"other"}""");

        var replaced = await database.GetAsync(SiteA);
        replaced.ApiKey.ShouldBeNull();
        replaced.Passphrase.ShouldBe("other");
        replaced.StorageAccount.ShouldBe("kept-account");
        replaced.MaxUploadMb.ShouldBe(25);

        // A typed write names every property, so it replaces them all.
        await database.SetAsync(SiteA, new MediaSettings { StorageAccount = null });
        (await database.GetAsync(SiteA)).StorageAccount.ShouldBeNull();

        // A row that does not exist yet has nothing to keep: the secret takes its code default.
        await WriteDocumentAsync(database, "media", SiteB, """{"maxUploadMb":4}""");
        (await database.GetAsync(SiteB)).StorageAccount.ShouldBe("default-account-8841");
    }

    [Fact]
    public async Task ConcurrentWholeDocumentWritesKeepTheSecretAndEachOtherIntact()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret + "-key" });

        await Task.WhenAll(Enumerable.Range(1, 10).Select(number => Task.Run(() => WriteDocumentAsync(database, "media", SiteA, $$"""{"maxUploadMb":{{number}}}"""))));

        var row = database.Rows().ShouldHaveSingleItem();
        row.Version.ShouldBe(11);
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret + "-key");
    }

    [Fact]
    public async Task TheRedactedResolveHasNoSecretValueAndNamesTheSecretsThatAreSet()
    {
        using var database = new SettingsDatabase();

        Task<RedactedSetting> ResolveAsync(SiteId? site) => database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, site, Token));

        var code = await ResolveAsync(SiteA);
        code.Source.ShouldBe(SettingsSource.Code);
        code.SecretsSet.ShouldBeEmpty();
        code.Json.ShouldNotContain("default-account-8841");
        JsonNode.Parse(code.Json)!.AsObject().Select(property => property.Key).ShouldBe(["maxUploadMb", "provider", "quality", "tags"], ignoreOrder: true);

        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-default", StorageAccount = "", Passphrase = null });
        var fromDefault = await ResolveAsync(SiteA);
        fromDefault.Source.ShouldBe(SettingsSource.Default);
        fromDefault.SecretsSet.ShouldBe(["apiKey"]);
        fromDefault.Json.ShouldNotContain(Secret);

        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret + "-key", StorageAccount = Secret + "-account", Passphrase = "pass" });
        var own = await ResolveAsync(SiteA);
        own.Source.ShouldBe(SettingsSource.Site);
        own.Version.ShouldBe(1);
        own.SecretsSet.ShouldBe(["apiKey", "passphrase", "storageAccount"]);
        own.Json.ShouldNotContain(Secret);
        own.Json.ShouldNotContain("pass\"");
        JsonNode.Parse(own.Json)!["maxUploadMb"]!.GetValue<int>().ShouldBe(25);
    }

    [Fact]
    public async Task ADocumentTheManagementEndpointShowsCanBeSentBackWithoutLosingASecret()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret + "-key" });

        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        await database.ServiceAsync(async (service, descriptor) =>
        {
            await service.WriteAsync(descriptor, SiteA, shown.Json, shown.Version, Token);
            return true;
        });

        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret + "-key");
    }

    [Fact]
    public void ADeletedSiteStopsAReadThatWasUnderWayFromCachingItsDocument()
    {
        using var database = new SettingsDatabase();
        var registry = new SettingsRegistry([typeof(MediaSettings)], TimeSpan.FromMinutes(1));
        var cache = new SettingsCache(registry, database.Clock);
        var site = Guid.CreateVersion7();
        var resolved = new ResolvedSetting("{}", SettingsSource.Site, 1);

        var generation = cache.Generation("media");
        cache.InvalidateSite(site);
        cache.Set(site, "media", resolved, generation);
        cache.TryGet(site, "media", out _).ShouldBeFalse();

        cache.Set(site, "media", resolved, cache.Generation("media"));
        cache.TryGet(site, "media", out _).ShouldBeTrue();
        cache.InvalidateSite(site);
        cache.TryGet(site, "media", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task TheFirstSiteWriteKeepsTheSecretOfTheInstanceDefaultThatTheSiteWasShown()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-default" });

        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        shown.Source.ShouldBe(SettingsSource.Default);
        shown.SecretsSet.ShouldContain("apiKey");
        shown.Version.ShouldBe(0);

        await WriteDocumentAsync(database, "media", SiteA, shown.Json, shown.Version);

        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret + "-default");
        database.Rows().Count.ShouldBe(2);

        // The site now has a row of its own, and its version is the one a second write names.
        var again = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        again.Source.ShouldBe(SettingsSource.Site);
        again.Version.ShouldBe(1);
        await WriteDocumentAsync(database, "media", SiteA, again.Json, again.Version);
        database.Rows().Single(row => row.SiteId == SiteA.Value).Version.ShouldBe(2);
    }

    [Fact]
    public async Task TheVersionOfTheRedactedResolveIsThatOfTheRowAWriteWouldReplace()
    {
        using var database = new SettingsDatabase();

        Task<RedactedSetting> ResolveAsync(SiteId? site) => database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, site, Token));

        (await ResolveAsync(null)).Version.ShouldBe(0);
        await database.SetDefaultAsync(new MediaSettings());
        await database.SetDefaultAsync(new MediaSettings { MaxUploadMb = 2 });

        var instance = await ResolveAsync(null);
        instance.Source.ShouldBe(SettingsSource.Default);
        instance.Version.ShouldBe(2);
        await WriteDocumentAsync(database, "media", null, instance.Json, instance.Version);

        (await ResolveAsync(SiteB)).Version.ShouldBe(0);
    }

    [Fact]
    public async Task AStoredSecretThatNoLongerHoldsItsAttributesIsNotKeptAndTheOthersAre()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", """{"maxUploadMb":7,"passphrase":"far-too-long-to-pass","storageAccount":"kept-account"}""");

        await WriteDocumentAsync(database, "media", SiteA, """{"maxUploadMb":8}""");

        var site = await database.GetAsync(SiteA);
        site.MaxUploadMb.ShouldBe(8);
        site.Passphrase.ShouldBeNull();
        site.StorageAccount.ShouldBe("kept-account");

        database.Logs.All.ShouldContain("passphrase");
        database.Logs.All.ShouldNotContain("far-too-long-to-pass");
        database.Logs.All.ShouldNotContain("kept-account");
    }

    [Fact]
    public async Task AWriteThatLeavesOutASecretOfARowTheClassCannotReadIsNotRefusedAndTakesNoSecretFromIt()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", """{"maxUploadMb":7,"apiKey":5,"storageAccount":"hidden-account"}""");

        await WriteDocumentAsync(database, "media", SiteA, """{"maxUploadMb":8}""");

        var site = await database.GetAsync(SiteA);
        site.MaxUploadMb.ShouldBe(8);
        site.ApiKey.ShouldBeNull();
        site.StorageAccount.ShouldBe("default-account-8841");
    }

    [Fact]
    public async Task TheSecretsOfASkippedRowAreReportedAsLostByKeyAndNeverByValue()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", $$"""{"maxUploadMb":"x","apiKey":"{{Secret}}-a","storageAccount":"{{Secret}}-b","passphrase":null}""");

        await WriteDocumentAsync(database, "media", SiteA, """{"maxUploadMb":8,"storageAccount":"sent"}""");

        var logs = database.Logs.All;
        logs.ShouldContain("secret key apiKey");
        logs.ShouldNotContain("secret key storageAccount");
        logs.ShouldNotContain("secret key passphrase");
        logs.ShouldNotContain(Secret);
        (await database.GetAsync(SiteA)).StorageAccount.ShouldBe("sent");
    }

    [Fact]
    public async Task AnUnreadableSiteRowDoesNotSupplyASecretThatTheReadDidNotShow()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-shown" });
        database.AddRow(SiteA.Value, "media", $$"""{"maxUploadMb":"x","apiKey":"{{Secret}}-hidden"}""");

        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        shown.Source.ShouldBe(SettingsSource.Default);
        shown.SecretsSet.ShouldContain("apiKey");
        shown.Version.ShouldBe(1);

        await WriteDocumentAsync(database, "media", SiteA, shown.Json, shown.Version);

        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret + "-shown");
    }

    [Fact]
    public async Task AnUnreadableInstanceRowSuppliesNoSecretToAWriteOfItself()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", $$"""{"maxUploadMb":"x","apiKey":"{{Secret}}"}""");

        await WriteDocumentAsync(database, "media", null, """{"maxUploadMb":8}""", 1);

        (await database.GetDefaultAsync()).ApiKey.ShouldBeNull();
    }

    [Fact]
    public void ARenamedPropertyIsStillSecretByItsNameInCSharp()
    {
        var descriptor = new SettingsRegistry([typeof(RenamedSecret)], TimeSpan.Zero).Find("renamed")!;

        descriptor.SecretKeys.ShouldBe(["pw"]);
        descriptor.CreateSchema()["properties"]!["pw"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task AnErrorUnderASecretDictionaryNamesTheSecretAndNotItsKey()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<SecretDictionary>());

        var errors = (await Should.ThrowAsync<ValidationException>(() => WriteDocumentAsync(database, "secret-dictionary", SiteA, """{"tokens":{"sk_live_ABC123":"x"}}"""))).Errors;

        var error = errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("tokens");
        error.Message.ShouldNotContain("sk_live_ABC123");
    }

    [Theory]
    [InlineData("apiKey")]
    [InlineData("ApiKey")]
    [InlineData("APIKey")]
    [InlineData("api_key")]
    [InlineData("stripeApiKey")]
    [InlineData("smtpPassword")]
    [InlineData("password")]
    [InlineData("passphrase")]
    [InlineData("accessToken")]
    [InlineData("token")]
    [InlineData("clientSecret")]
    [InlineData("secret")]
    [InlineData("privateKey")]
    [InlineData("secretKey")]
    [InlineData("accessKey")]
    [InlineData("connectionString")]
    [InlineData("databaseConnectionString")]
    [InlineData("Token2")]
    [InlineData("ApiKey2")]
    [InlineData("PasswordV2")]
    [InlineData("password_v10")]
    [InlineData("Tokens")]
    [InlineData("Passwords")]
    [InlineData("Secrets")]
    [InlineData("ApiKeys")]
    [InlineData("ConnectionStrings")]
    [InlineData("APIKEY")]
    [InlineData("apikey")]
    [InlineData("SigningKey")]
    [InlineData("EncryptionKey")]
    [InlineData("Pwd")]
    public void ANameThatEndsLikeASecretIsASecret(string name) => SettingsSecretNames.Matches(name).ShouldBeTrue();

    [Theory]
    [InlineData("keywords")]
    [InlineData("Keywords")]
    [InlineData("sortKey")]
    [InlineData("hotkey")]
    [InlineData("primaryKey")]
    [InlineData("key")]
    [InlineData("tokenizer")]
    [InlineData("passwordPolicyEnabled")]
    [InlineData("secretsPath")]
    [InlineData("monkey")]
    [InlineData("Tokenizer")]
    [InlineData("Hotkey")]
    [InlineData("PrimaryKey")]
    [InlineData("AccessKeyId")]
    [InlineData("TokenLifetime")]
    [InlineData("PasswordMinLength")]
    [InlineData("SecretaryEmail")]
    [InlineData("ApiKeyHeaderName")]
    [InlineData("v2")]
    [InlineData("title")]
    [InlineData("")]
    public void ANameThatOnlyContainsASecretWordIsNot(string name) => SettingsSecretNames.Matches(name).ShouldBeFalse();

    [Fact]
    public void AKeywordsPropertyIsNotASecretAtAnyLevel()
    {
        var descriptor = Should.NotThrow(() => new SettingsRegistry([typeof(SeoKeywords)], TimeSpan.Zero)).Find("seo-keywords")!;

        descriptor.SecretKeys.ShouldBeEmpty();
        descriptor.CreateSchema()["properties"]!["keywords"]!.AsObject().ContainsKey("writeOnly").ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(long))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(bool?))]
    [InlineData(typeof(TokenKind))]
    [InlineData(typeof(TokenKind?))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<int?>))]
    [InlineData(typeof(IReadOnlyList<TokenKind>))]
    [InlineData(typeof(HashSet<bool>))]
    public void AValueThatCannotBeTextIsOutsideTheNameRule(Type type) => SettingsSecretNames.AppliesTo(type).ShouldBeFalse();

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(Dictionary<string, string>))]
    [InlineData(typeof(Dictionary<string, int>))]
    [InlineData(typeof(IReadOnlyDictionary<string, bool>))]
    [InlineData(typeof(List<Dictionary<string, int>>))]
    [InlineData(typeof(byte[]))]
    [InlineData(typeof(ReadOnlyMemory<byte>))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(Uri))]
    [InlineData(typeof(Credentials))]
    [InlineData(typeof(object))]
    [InlineData(typeof(System.Text.Json.JsonElement))]
    public void AValueThatCanBeTextIsUnderTheNameRule(Type type) => SettingsSecretNames.AppliesTo(type).ShouldBeTrue();

    [Fact]
    public void ANumberABooleanAnEnumOrADateIsNeverASecretByItsNameAndAStringStillIs()
    {
        var descriptor = Should.NotThrow(() => new SettingsRegistry([typeof(NamedLikeSecretsButNotText)], TimeSpan.Zero)).Find("named-not-text")!;

        descriptor.SecretKeys.Order(StringComparer.Ordinal).ShouldBe(["pin", "smtpPassword"]);

        var properties = descriptor.CreateSchema()["properties"]!.AsObject();
        foreach (var key in new[] { "maxOutputTokens", "hasApiKey", "accessToken", "rotatedSecret", "retryTokens", "budget" })
        {
            properties[key]!.AsObject().ContainsKey("writeOnly").ShouldBeFalse(key);
        }

        properties["maxOutputTokens"]!["default"]!.GetValue<int>().ShouldBe(1024);
        properties["budget"]!["properties"]!["maxTokens"]!.AsObject().ContainsKey("writeOnly").ShouldBeFalse();
        properties["smtpPassword"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
        properties["pin"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
        properties["pin"]!.AsObject().ContainsKey("default").ShouldBeFalse();
    }

    [Fact]
    public async Task TheRedactedVersionOfARowThatCannotBeReadCanBeWrittenBack()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", """{"maxUploadMb":"x"}""");
        database.AddRow(null, "media", """{"maxUploadMb":"y"}""");

        Task<RedactedSetting> ResolveAsync(SiteId? site) => database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, site, Token));

        var site = await ResolveAsync(SiteA);
        site.Source.ShouldBe(SettingsSource.Code);
        site.Version.ShouldBe(1);
        await WriteDocumentAsync(database, "media", SiteA, """{"maxUploadMb":4}""", site.Version);

        var instance = await ResolveAsync(null);
        instance.Source.ShouldBe(SettingsSource.Code);
        instance.Version.ShouldBe(1);
        await WriteDocumentAsync(database, "media", null, """{"maxUploadMb":5}""", instance.Version);

        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(4);
        (await database.GetDefaultAsync()).MaxUploadMb.ShouldBe(5);
        database.Rows().ShouldAllBe(row => row.Version == 2);
    }

    [Fact]
    public async Task ASiteWithNoRowOfItsOwnStillNamesVersionZeroWhenTheInstanceRowCannotBeRead()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", """{"maxUploadMb":"y"}""");

        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));

        shown.Version.ShouldBe(0);
        await WriteDocumentAsync(database, "media", SiteA, shown.Json, shown.Version);
    }

    [Fact]
    public async Task TheFirstSiteWriteInheritsNothingFromAnInstanceRowTheReadRejected()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", $$"""{"maxUploadMb":"x","apiKey":"{{Secret}}"}""");

        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        shown.Source.ShouldBe(SettingsSource.Code);
        shown.SecretsSet.ShouldBeEmpty();

        await WriteDocumentAsync(database, "media", SiteA, shown.Json, shown.Version);

        (await database.GetAsync(SiteA)).ApiKey.ShouldBeNull();
    }

    [Fact]
    public async Task AnInheritedSecretIsACopyThatARotationOfTheInstanceDefaultDoesNotReach()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-old" });
        var shown = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        await WriteDocumentAsync(database, "media", SiteA, shown.Json, shown.Version);

        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-new" });

        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret + "-old");
        (await database.GetAsync(SiteB)).ApiKey.ShouldBe(Secret + "-new");
    }
}
