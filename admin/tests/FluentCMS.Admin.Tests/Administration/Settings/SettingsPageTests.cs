using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Settings;

// The settings menu and the generated settings pages (spec 4.16, A-34): the areas the caller may manage, a form from the
// schema of the read with the field editors of 4.6, the source of the area, secrets that are never read and sent only
// when typed, save and reset over the version read, the conflict, and the settings page of an admin half.
public sealed class SettingsPageTests : AdministrationTestContext
{
    private const string Typed = "typed-secret-0c1d";

    private readonly FakeSettings settings = new();

    public SettingsPageTests()
    {
        UseNeutralResources();
        Management.Respond = settings.Respond;
    }

    internal static JsonObject BlogSchema() => new()
    {
        ["type"] = "object",
        ["title"] = "blog",
        ["properties"] = new JsonObject
        {
            ["siteName"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 100, ["default"] = "My blog" },
            ["articlesPerPage"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 10 },
            ["apiKey"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 512, ["writeOnly"] = true },
        },
        ["required"] = new JsonArray("siteName"),
    };

    internal static JsonObject BlogValue(string name = "My blog", int perPage = 10) => new() { ["siteName"] = name, ["articlesPerPage"] = perPage };

    [Fact]
    public void TheMenuListsTheAreasTheCallerMayManageUnderCoreOrTheirPluginEachLinkedToItsScope()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        settings.Areas.Add(FakeSettings.Area("branding", site: true, defaults: true));
        settings.Areas.Add(FakeSettings.Area("media", "media", site: false, defaults: true));
        settings.Areas.Add(FakeSettings.Area("ai", "ai", site: true, defaults: true));
        settings.Areas.Add(FakeSettings.Area("mcp", "mcp", site: true, defaults: false));
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings");

        app.WaitForAssertion(() => app.FindAll("main h2").Select(heading => heading.TextContent).ShouldBe(["Core", "Plugin ai", "Plugin media"]));
        app.FindAll("main .settings-areas a").Select(link => (link.TextContent, link.GetAttribute("href")))
            .ShouldBe([("Blog", "settings/blog"), ("Branding", "settings/branding/defaults"), ("AI assist", "settings/ai"), ("Media", "settings/media/defaults")]);

        // mcp is read from the defaults, which this caller may not manage, so it offers nothing.
        app.FindAll("main .settings-area-scope").Select(scope => scope.TextContent)
            .ShouldBe(["This site", "The defaults of the instance, which every site reads", "This site and the defaults of the instance", "The defaults of the instance"]);
    }

    [Fact]
    public void AnAdministratorChangesASettingOfTheSiteSeesItAsTheSitesOwnResetsItAndSeesTheDefaultAgain()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var blog = settings.Add("blog", BlogSchema(), BlogValue(), source: "default");
        blog.ResetTo = BlogValue();
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("default"));
        Heading(app).ShouldBe("Blog");
        Button(app, "Save").HasAttribute("disabled").ShouldBeTrue();
        app.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldNotContain("Reset to the defaults");

        Input(app, "Site name").Input("Shop news");
        Button(app, "Save").Click();

        app.WaitForAssertion(() => Source(app).ShouldBe("site"));
        app.Find(".settings-source").TextContent.ShouldBe("These are this site's own settings.");
        app.Find(".settings-message").TextContent.ShouldBe("The settings are saved.");
        var (method, path, ifMatch, body) = settings.Writes.ShouldHaveSingleItem();
        (method, path, ifMatch).ShouldBe((HttpMethod.Put, "settings/blog", "\"0\""));
        JsonNode.DeepEquals(body, BlogValue("Shop news")).ShouldBeTrue(body!.ToJsonString());

        Button(app, "Reset to the defaults").Click();
        app.Find("fluent-dialog[aria-label='Reset the settings'] p").TextContent.ShouldContain("follows the defaults of the instance again");
        Button(app, "Reset").Click();

        app.WaitForAssertion(() => Source(app).ShouldBe("default"));
        settings.Writes[^1].ShouldBe((HttpMethod.Delete, "settings/blog", "\"1\"", null));
        Input(app, "Site name").GetAttribute("value").ShouldBe("My blog");
        app.Find(".settings-message").TextContent.ShouldBe("The settings are reset.");
    }

    [Fact]
    public void TheDefaultsAreChangedByACallerWhoMayAndTheSwitchShowsOnlyToOneWhoMayManageBoth()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: true));
        settings.Add("blog", BlogSchema(), BlogValue(), source: "code");
        settings.Add("blog/defaults", BlogSchema(), BlogValue(), source: "code");
        GrantArea("settings.blog.manage", instanceOnly: true);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("code"));
        var scopes = app.Find("nav.settings-scopes").QuerySelectorAll("a");
        scopes.Select(link => (link.TextContent, link.GetAttribute("href"), link.GetAttribute("aria-current")))
            .ShouldBe([("This site", "settings/blog", "page"), ("Defaults of the instance", "settings/blog/defaults", null)]);

        Navigation.NavigateTo("settings/blog/defaults");
        app.WaitForAssertion(() => app.Find("nav.settings-scopes a[aria-current=page]").TextContent.ShouldBe("Defaults of the instance"));
        app.WaitForAssertion(() => app.Find(".settings-source").TextContent.ShouldBe("The instance follows the code's defaults. Saving makes them the instance's own."));
        Input(app, "Articles per page").ShouldNotBeNull();
        Input(app, "Site name").Input("Everywhere");
        Button(app, "Save").Click();

        app.WaitForAssertion(() => Source(app).ShouldBe("default"));
        settings.Writes.ShouldHaveSingleItem().Path.ShouldBe("settings/blog/defaults");
        Button(app, "Reset to the code's defaults").ShouldNotBeNull();
    }

    [Fact]
    public void ACallerWhoMayManageTheSiteButNotTheDefaultsSeesNoSwitchAndTheDefaultsRouteSaysSoWithoutAReadOfThem()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        settings.Add("blog", BlogSchema(), BlogValue());
        settings.Add("blog/defaults", BlogSchema(), BlogValue());
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("default"));
        app.FindAll("nav.settings-scopes").ShouldBeEmpty();

        Navigation.NavigateTo("settings/blog/defaults");

        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain("There is no such settings area, or your roles do not let you manage it here."));
        app.FindAll("form, .settings-fields").ShouldBeEmpty();
        Management.Count("settings/blog/defaults").ShouldBe(0);
    }

    [Fact]
    public void AnAreaReadFromTheDefaultsOnlyOpensItsDefaults()
    {
        settings.Areas.Add(FakeSettings.Area("mcp", "mcp", site: true, defaults: true));
        settings.Add("mcp/defaults", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["maxClients"] = new JsonObject { ["type"] = "integer" } } }, new JsonObject { ["maxClients"] = 1000 }, source: "code");
        GrantArea("settings.mcp.manage", instanceOnly: true);

        var app = Load("settings/mcp");

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("settings/mcp/defaults"));
        app.WaitForAssertion(() => Source(app).ShouldBe("code"));
        app.FindAll("nav.settings-scopes").ShouldBeEmpty();
        app.Find("main").TextContent.ShouldContain("These are the defaults of the instance.");
        Management.Count("settings/mcp").ShouldBe(0);
    }

    [Fact]
    public void EachKindOfSchemaPropertyIsReadIntoTheFieldOfItsEditor()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 100, ["pattern"] = "^[a-z]+$", ["description"] = "The name." },
                ["count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 9223372036854775807 },
                ["optional"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                ["ratio"] = new JsonObject { ["type"] = "number" },
                ["on"] = new JsonObject { ["type"] = "boolean" },
                ["provider"] = new JsonObject { ["enum"] = new JsonArray("none", "openAi", null) },
                ["kinds"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["enum"] = new JsonArray("a", "b") } },
                ["when"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["day"] = new JsonObject { ["type"] = "string", ["format"] = "date" },
                ["mail"] = new JsonObject { ["type"] = "string", ["format"] = "email" },
                ["site"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["format"] = "uri" },
                ["types"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["fields"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "array" } },
                ["mixed"] = new JsonObject { ["type"] = new JsonArray("string", "integer") },
                ["token"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true },
            },
            ["required"] = new JsonArray("name"),
        };

        var fields = FluentCMS.Admin.Administration.Settings.SettingsFields.Of(schema);

        fields.Select(field => (field.Field.Name, field.Field.Type, field.Field.Multiple, field.Secret)).ShouldBe(
        [
            ("name", "text", false, false), ("count", "integer", false, false), ("optional", "integer", false, false), ("ratio", "decimal", false, false),
            ("on", "boolean", false, false), ("provider", "select", false, false), ("kinds", "select", true, false), ("when", "dateTime", false, false),
            ("day", "date", false, false), ("mail", "email", false, false), ("site", "url", false, false), ("types", "json", false, false),
            ("fields", "json", false, false), ("mixed", "json", false, false), ("token", "text", false, true),
        ]);
        var name = fields[0].Field;
        (name.Validation.Required, name.Validation.MinLength, name.Validation.MaxLength, name.Validation.Pattern, name.Display?.Help).ShouldBe((true, 1, 100, "^[a-z]+$", "The name."));
        (fields[1].Field.Validation.Minimum, fields[1].Field.Validation.Maximum, fields[1].Field.Validation.Required).ShouldBe((1m, 9223372036854775807m, false));
        fields[5].Field.Settings["options"]!.AsArray().Select(option => option!.GetValue<string>()).ShouldBe(["none", "openAi"]);
        fields[6].Field.Settings["options"]!.AsArray().Select(option => option!.GetValue<string>()).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void EachKindOfSchemaPropertyIsDrawnWithTheEditorOfItsFieldType()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["count"] = new JsonObject { ["type"] = "integer" },
                ["on"] = new JsonObject { ["type"] = "boolean" },
                ["provider"] = new JsonObject { ["enum"] = new JsonArray("none", "openAi") },
                ["when"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["types"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["token"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true },
            },
        };
        var value = new JsonObject { ["name"] = "x", ["count"] = 3, ["on"] = true, ["provider"] = "openAi", ["when"] = null, ["types"] = new JsonArray("image/png") };
        settings.Areas.Add(FakeSettings.Area("kinds", site: true, defaults: false));
        settings.Add("kinds", schema, value);
        GrantArea("settings.kinds.manage", instanceOnly: false);

        var app = Load("settings/kinds");

        app.WaitForAssertion(() => app.FindComponents<TextFieldEditor>().Count.ShouldBe(1));
        app.FindComponents<NumberFieldEditor>().Count.ShouldBe(1);
        app.FindComponents<BooleanFieldEditor>().Count.ShouldBe(1);
        app.FindComponents<SelectFieldEditor>().Single().Instance.Value!.GetValue<string>().ShouldBe("openAi");
        app.FindComponents<DateTimeFieldEditor>().Count.ShouldBe(1);
        app.FindComponents<JsonFieldEditor>().Single().Instance.Field.Name.ShouldBe("types");
        Input(app, "Token").GetAttribute("type").ShouldBe("password");
    }

    [Fact]
    public void ASecretShowsAsSetOrNotSetNeverHoldsAValueAndASaveOfAnotherFieldLeavesItOutOfTheRequest()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var schema = BlogSchema();
        schema["properties"]!["token"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true };
        // A read never holds a secret; one that held it anyway would not show it nor send it back.
        var value = BlogValue();
        value["apiKey"] = "stored-value";
        var blog = settings.Add("blog", schema, value, source: "site", version: 4);
        blog.SecretsSet = ["apiKey"];
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));

        var apiKey = Input(app, "Api key");
        (apiKey.GetAttribute("type"), apiKey.GetAttribute("value") ?? "", apiKey.GetAttribute("autocomplete")).ShouldBe(("password", "", "new-password"));
        Help(app, "Api key").ShouldBe("Set. A value typed here replaces it.");

        // A value the key ring cannot read is in neither list (#3379), so a secret that is not set says it may be one.
        Help(app, "Token").ShouldBe("Not set. A value typed here sets it. A value the key ring can no longer read shows as not set too; the startup report on the diagnostics page lists such areas under settings-secrets-unreadable.");

        Input(app, "Articles per page").Change("20");
        Button(app, "Save").Click();

        app.WaitForAssertion(() => settings.Writes.Count.ShouldBe(1));
        settings.Writes[0].Body!["articlesPerPage"]!.GetValue<long>().ShouldBe(20);
        var body = settings.Writes[0].Body!;
        body.Select(property => property.Key).ShouldBe(["siteName", "articlesPerPage"]);
        app.WaitForAssertion(() => Help(app, "Api key").ShouldBe("Set. A value typed here replaces it."));
        app.Markup.ShouldNotContain("stored-value");
    }

    [Fact]
    public void ATypedSecretIsSentOnceAndForgottenAndReachesNoLogNoDraftAndNoBrowserStorage()
    {
        var log = new CapturedLog();
        Services.AddSingleton<ILoggerFactory>(new LoggerFactory([log]));
        Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        settings.Add("blog", BlogSchema(), BlogValue(), source: "site", version: 2);
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));
        Help(app, "Api key").ShouldStartWith("Not set.");

        Input(app, "Api key").Input(Typed);
        Button(app, "Save").IsDisabled().ShouldBeFalse();

        // An error of the save keeps what was typed, without showing it, so that the user can save again.
        settings.NextWrite = Problem(HttpStatusCode.UnprocessableEntity, "FCMS2805", "articlesPerPage");
        Button(app, "Save").Click();
        app.WaitForAssertion(() => app.Find(".settings-message").TextContent.ShouldBe("The settings are not saved: some values are not valid."));
        Input(app, "Articles per page").GetAttribute("aria-invalid").ShouldBe("true");

        Button(app, "Save").Click();
        app.WaitForAssertion(() => Help(app, "Api key").ShouldBe("Set. A value typed here replaces it."));

        settings.Writes.Select(write => write.Body!["apiKey"]?.GetValue<string>()).ShouldBe([Typed, Typed]);
        (Input(app, "Api key").GetAttribute("value") ?? "").ShouldBeEmpty();
        app.Markup.ShouldNotContain(Typed);
        Button(app, "Save").IsDisabled().ShouldBeTrue();

        // A later save of another field names the secret no more.
        Input(app, "Site name").Input("Again");
        Button(app, "Save").Click();
        app.WaitForAssertion(() => settings.Writes.Count.ShouldBe(3));
        settings.Writes[2].Body!.ContainsKey("apiKey").ShouldBeFalse();

        Storage.Items.Values.ShouldAllBe(value => !value.Contains(Typed, StringComparison.Ordinal));
        JSInterop.Invocations.SelectMany(call => call.Arguments).Select(argument => argument?.ToString() ?? "").ShouldAllBe(argument => !argument.Contains(Typed, StringComparison.Ordinal));
        log.Lines.ShouldAllBe(line => !line.Contains(Typed, StringComparison.Ordinal));
    }

    [Fact]
    public void AnInheritedSecretShowsAsInherited()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var schema = BlogSchema();
        schema["properties"]!["token"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true };
        var blog = settings.Add("blog", schema, BlogValue(), source: "default");
        (blog.SecretsSet, blog.SecretsInherited) = (["apiKey"], ["apiKey", "token"]);
        GrantArea("settings.blog.manage", instanceOnly: false);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("default"));

        Help(app, "Api key").ShouldBe("Set in the defaults of the instance, which this site inherits. A value typed here becomes the site's own.");
        Help(app, "Token").ShouldStartWith("Not set in the defaults of the instance, which this site inherits.");
        app.Find(".settings-source").TextContent.ShouldBe("This site follows the defaults of the instance. Saving makes the settings the site's own.");
    }

    [Fact]
    public void ASaveOverAChangeMadeElsewhereShowsTheConflictAndKeepMineSavesOverTheNewerVersion()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var blog = settings.Add("blog", BlogSchema(), BlogValue(), source: "site", version: 3);
        GrantArea("settings.blog.manage", instanceOnly: false);
        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));

        settings.BeforeWrite = () => (blog.Value, blog.Version) = (BlogValue("Their name", 30), 4);
        Input(app, "Site name").Input("My name");
        Input(app, "Api key").Input(Typed);
        Button(app, "Save").Click();

        var dialog = "fluent-dialog[aria-label='Changed elsewhere']";
        app.WaitForAssertion(() => app.Find($"{dialog} table"));
        app.FindAll($"{dialog} tbody tr").Select(row => string.Join("|", row.Children.Select(cell => cell.TextContent))).ShouldBe(["Site name|My name|Their name"]);
        app.Find(dialog).TextContent.ShouldContain("The secrets you typed are saved with your changes, and dropped with theirs.");
        app.Find(dialog).TextContent.ShouldNotContain(Typed);

        Button(app, "Keep mine").Click();

        app.WaitForAssertion(() => settings.Writes.Count.ShouldBe(2));
        var (_, _, ifMatch, body) = settings.Writes[1];
        ifMatch.ShouldBe("\"4\"");
        JsonNode.DeepEquals(body, new JsonObject { ["siteName"] = "My name", ["articlesPerPage"] = 30, ["apiKey"] = Typed }).ShouldBeTrue(body!.ToJsonString());
        app.WaitForAssertion(() => app.Find(".settings-message").TextContent.ShouldBe("The settings are saved."));
    }

    [Fact]
    public void TakeTheirsDropsTheChangesAndTheTypedSecretsAndShowsTheNewerVersion()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var blog = settings.Add("blog", BlogSchema(), BlogValue(), source: "site", version: 3);
        GrantArea("settings.blog.manage", instanceOnly: false);
        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));

        settings.BeforeWrite = () => (blog.Value, blog.Version) = (BlogValue("Their name", 30), 4);
        Input(app, "Site name").Input("My name");
        Input(app, "Api key").Input(Typed);
        Button(app, "Save").Click();
        app.WaitForAssertion(() => app.Find("fluent-dialog[aria-label='Changed elsewhere'] table"));

        Button(app, "Take theirs").Click();

        app.WaitForAssertion(() => Input(app, "Site name").GetAttribute("value").ShouldBe("Their name"));
        (Input(app, "Api key").GetAttribute("value") ?? "").ShouldBeEmpty();
        settings.Writes.Count.ShouldBe(1);
        app.Find(".settings-message").TextContent.ShouldBe("Your changes are dropped; the settings are shown as they are now.");
        Button(app, "Save").IsDisabled().ShouldBeTrue();
    }

    [Fact]
    public void AResetOverAChangeMadeElsewhereResetsNothingAndShowsTheNewerVersion()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: false));
        var blog = settings.Add("blog", BlogSchema(), BlogValue("Mine"), source: "site", version: 3);
        GrantArea("settings.blog.manage", instanceOnly: false);
        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));

        settings.BeforeWrite = () => (blog.Value, blog.Version) = (BlogValue("Theirs"), 4);
        Button(app, "Reset to the defaults").Click();
        Button(app, "Reset").Click();

        app.WaitForAssertion(() => app.Find(".settings-message").TextContent.ShouldBe("The settings changed since you opened them, so nothing was reset. They are shown as they are now."));
        Input(app, "Site name").GetAttribute("value").ShouldBe("Theirs");
        blog.Source.ShouldBe("site");
    }

    [Fact]
    public void TheSettingsPageOfTheAreasHalfShowsInPlaceOfTheGeneratedOneAndNotWhereItsPluginIsDisabled()
    {
        settings.Areas.Add(FakeSettings.Area("reviews", "reviews", site: true, defaults: false));
        settings.Add("reviews", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["moderated"] = new JsonObject { ["type"] = "boolean" } } }, new JsonObject { ["moderated"] = false });
        UseTheSampleHalf(enabled: true);
        GrantArea("settings.reviews.manage", instanceOnly: false);

        var app = Load("settings/reviews");

        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain("Moderation"));
        Heading(app).ShouldBe("Reviews");
        app.FindAll(".settings-fields").ShouldBeEmpty();
        Management.Count("settings/reviews").ShouldBe(0);
    }

    [Fact]
    public void WhereTheAreasPluginIsDisabledTheGeneratedPageShows()
    {
        settings.Areas.Add(FakeSettings.Area("reviews", "reviews", site: true, defaults: false));
        settings.Add("reviews", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["moderated"] = new JsonObject { ["type"] = "boolean" } } }, new JsonObject { ["moderated"] = false });
        UseTheSampleHalf(enabled: false);
        GrantArea("settings.reviews.manage", instanceOnly: false);

        var app = Load("settings/reviews");

        app.WaitForAssertion(() => app.FindComponents<BooleanFieldEditor>().Count.ShouldBe(1));
        app.FindAll("main .cms-card").ShouldBeEmpty();
        Loader.Loads.ShouldBe(1);
    }

    [Fact]
    public void AHalfThatIsNotTheAreasPluginDoesNotReplaceItsPage()
    {
        // The area is registered by core, not by the reviews plugin, whose half declares a page for it.
        settings.Areas.Add(FakeSettings.Area("reviews", site: true, defaults: false));
        settings.Add("reviews", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["moderated"] = new JsonObject { ["type"] = "boolean" } } }, new JsonObject { ["moderated"] = false });
        UseTheSampleHalf(enabled: true);
        GrantArea("settings.reviews.manage", instanceOnly: false);

        var app = Load("settings/reviews");

        app.WaitForAssertion(() => app.FindComponents<BooleanFieldEditor>().Count.ShouldBe(1));
        app.FindAll("main .cms-card").ShouldBeEmpty();
    }

    [Fact]
    public void EveryControlOfThePageIsReachedByTheKeyboard()
    {
        settings.Areas.Add(FakeSettings.Area("blog", site: true, defaults: true));
        settings.Add("blog", BlogSchema(), BlogValue(), source: "site", version: 1);
        GrantArea("settings.blog.manage", instanceOnly: true);

        var app = Load("settings/blog");
        app.WaitForAssertion(() => Source(app).ShouldBe("site"));

        // The switch is links, every value a native input with its label, every action a button of the library, and none of
        // them is taken out of the tab order.
        app.FindAll("main nav.settings-scopes a[href]").Count.ShouldBe(2);
        var inputs = app.FindAll("main .settings-fields input");
        inputs.Count.ShouldBe(3);
        inputs.ShouldAllBe(input => app.FindAll($"label[for='{input.Id}']").Count == 1);
        app.FindAll("main .settings-actions fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Save", "Reset to the defaults"]);
        app.FindAll("main a[tabindex], main input[tabindex], main fluent-button[tabindex]").ShouldBeEmpty();
    }

    // The sample half, which declares the settings page of the area reviews, as the app's one half.
    private void UseTheSampleHalf(bool enabled)
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled)];
    }

    private static string? Source(IRenderedComponent<FluentCMS.Admin.AdminApp> app) => app.FindAll(".settings-source").SingleOrDefault()?.GetAttribute("data-source");

    private static IElement Button(IRenderedComponent<FluentCMS.Admin.AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IElement Input(IRenderedComponent<FluentCMS.Admin.AdminApp> app, string label)
    {
        var found = app.FindAll("main label").Single(element => element.TextContent.Replace("*", "", StringComparison.Ordinal).Trim() == label);
        return app.Find($"#{found.GetAttribute("for")}");
    }

    // The message under an input, its help or its error.
    private static string Help(IRenderedComponent<FluentCMS.Admin.AdminApp> app, string label) => app.Find($"#{Input(app, label).Id}-message").TextContent.Trim();

    private sealed class CapturedLog : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        // bUnit's test renderer logs the arguments of each event it dispatches, as the browser's renderer does not.
        public ILogger CreateLogger(string categoryName) => categoryName.StartsWith("Bunit", StringComparison.Ordinal) ? NullLogger.Instance : new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLog log) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (log.Lines)
                {
                    log.Lines.Add($"{formatter(state, exception)} {state} {exception}");
                }
            }
        }
    }
}

internal static class ElementExtensions
{
    public static bool IsDisabled(this IElement element) => element.HasAttribute("disabled");
}
