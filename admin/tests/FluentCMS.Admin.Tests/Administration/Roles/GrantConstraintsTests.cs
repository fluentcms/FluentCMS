using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Administration.Roles;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// The constraints of a grant in the role editor (spec 4.15, rows 4.4 to 4.7): what each offers, the request each makes, a
// limit that does not narrow left out, the refusal of one on its field, and every constraint the user did not touch, the
// resources among them, sent back as read.
public sealed class GrantConstraintsTests : RolesTestContext
{
    public GrantConstraintsTests()
    {
        UseNeutralResources();
        Management.AddLocale(MainSite, "fr", 1);
    }

    [Fact]
    public void AnAdministratorLimitsAGrantOfAContentTypeToOneSiteTwoLocalesAndThreeFieldsAndReadsTheSameBack()
    {
        Permissions.Add(Permission("content.article.update", "Edit article entries.", "content"));
        var app = Open("roles/new");
        app.WaitForAssertion(() => Field(app, "Name"));
        Field(app, "Name").Input("Translators");
        var site = app.FindAll("main form label").Single(label => label.TextContent.Trim() == "Site");
        app.Find($"#{site.GetAttribute("for")}").Change("every-site");
        Add(app, GrantCatalogue.Content, "content.article.update");
        app.WaitForAssertion(() => Row(app, "content.article.update"));

        var row = Row(app, "content.article.update");
        Labels(Group(row, "Sites")).ShouldBe(["Main", "Shop"]);
        Labels(Group(row, "Locales")).Select(label => label[..2]).ShouldBe(["en", "fr"]);
        Labels(Group(row, "Fields")).ShouldBe(["title", "body", "summary"]);
        Check(app, "content.article.update", "Sites", "Main");
        Check(app, "content.article.update", "Locales", "en");
        Check(app, "content.article.update", "Locales", "fr");
        Check(app, "content.article.update", "Fields", "title");
        Check(app, "content.article.update", "Fields", "body");
        Check(app, "content.article.update", "Fields", "summary");
        Save(app);

        app.WaitForAssertion(() => Heading(app).ShouldBe("Translators"));
        var expected = new JsonObject
        {
            ["sites"] = new JsonArray(MainSite.ToString("D")),
            ["locales"] = new JsonArray("en", "fr"),
            ["fields"] = new JsonArray("title", "body", "summary"),
        };
        var sent = LastWrite(HttpMethod.Post);
        sent["siteId"].ShouldBeNull();
        JsonNode.DeepEquals(sent["grants"], new JsonArray(new JsonObject { ["permission"] = "content.article.update", ["constraints"] = expected.DeepClone() }))
            .ShouldBeTrue(sent["grants"]!.ToJsonString());

        // Read back: the role as the server answers it shows the same limits, checked.
        app.WaitForAssertion(() => Checked(Group(Row(app, "content.article.update"), "Sites")).ShouldBe(["Main"]));
        Checked(Group(Row(app, "content.article.update"), "Locales")).Select(label => label[..2]).ShouldBe(["en", "fr"]);
        Checked(Group(Row(app, "content.article.update"), "Fields")).ShouldBe(["title", "body", "summary"]);
        Row(app, "content.article.update").QuerySelector("dl")!.TextContent.ShouldContain("en, fr");
    }

    // A role of a site grants in its site only, so no site is offered, and its locales are that site's.
    [Fact]
    public void ARoleOfASiteOffersNoSitesAndTheLocalesOfItsSite()
    {
        Roles.Add(Role(ShopEditors2, Shop, "Shop writers", builtIn: false, Grant("content.article.read")));
        var app = Open($"roles/{ShopEditors2:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        var row = Row(app, "content.article.read");
        Groups(row).ShouldBe(["Locales", "Fields"]);
        Labels(Group(row, "Locales")).Select(label => label[..2]).ShouldBe(["en"]);
    }

    // The fields of a pattern over every type are not offered, nor fields and a row rule for a grant that is not content's.
    [Fact]
    public void FieldsAreOfferedForAGrantOfOneTypeAndARowRuleForAContentGrantOnly()
    {
        Roles.Add(Role(ShopEditors2, null, "Readers", builtIn: false, Grant("content.*.read"), Grant("schema.read"), Grant("content.article.*")));
        var app = Open($"roles/{ShopEditors2:D}");
        app.WaitForAssertion(() => Row(app, "content.*.read"));

        Groups(Row(app, "content.*.read")).ShouldBe(["Sites", "Locales"]);
        Groups(Row(app, "schema.read")).ShouldBe(["Sites", "Locales"]);
        Groups(Row(app, "content.article.*")).ShouldBe(["Sites", "Locales", "Fields"]);
        Row(app, "schema.read").QuerySelectorAll("select").ShouldBeEmpty();
        Row(app, "content.article.*").TextContent.ShouldContain("Fields you cannot read are not listed.");
        Options(RuleSelect(Row(app, "content.*.read"))).ShouldBe(["No row rule", "own-entries"]);
        Options(RuleSelect(Row(app, "content.article.*"))).ShouldBe(["No row rule", "own-entries", "in-review"]);
    }

    [Fact]
    public void ARowRuleIsPickedFromTheRulesOfTheTypeAndTakenOff()
    {
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));
        Management.Count("permissions/row-rules").ShouldBe(1);

        RuleSelect(Row(app, "content.article.read")).Change("in-review");
        Save(app);
        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        LastWrite(HttpMethod.Put)["grants"]![1]!["constraints"]!["rowRule"]!.GetValue<string>().ShouldBe("in-review");

        RuleSelect(Row(app, "content.article.read")).Change(string.Empty);
        Save(app);
        app.WaitForAssertion(() => Writes.Count(write => write.Method == HttpMethod.Put).ShouldBe(2));
        LastWrite(HttpMethod.Put)["grants"]![1]!["constraints"]!.AsObject().ContainsKey("rowRule").ShouldBeFalse();
    }

    // A stored rule that is no longer registered shows by its name, and the server's refusal of it goes to its field.
    [Fact]
    public void ARowRuleThatIsNotRegisteredIsRefusedByNameOnItsField()
    {
        RoleOf(Reviewers)["grants"]![1]!["constraints"]!["rowRule"] = "retired";
        Refuse = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.BadRequest, "FCMS3042", "grants[1].constraints.rowRule") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));
        Options(RuleSelect(Row(app, "content.article.read"))).ShouldContain("retired (not registered for this grant)");

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was not saved: the server refused the grants marked."));
        var row = Row(app, "content.article.read");
        row.QuerySelector("details")!.HasAttribute("open").ShouldBeTrue();
        var select = RuleSelect(row);
        select.GetAttribute("aria-invalid").ShouldBe("true");
        select.GetAttribute("aria-describedby")!.Split(' ').Select(id => row.QuerySelector($"#{id}")!.TextContent)
            .ShouldContain("There is no registered row rule retired, so the server refused it.");
        Group(row, "Fields").QuerySelectorAll(".cms-field-message-error").ShouldBeEmpty();
        Row(app, "media.read").QuerySelector("details")!.HasAttribute("open").ShouldBeFalse();
    }

    // FCMS2311 on grants[i].constraints.locales: the locales of that grant only, and no other field, are marked.
    [Fact]
    public void ARefusalOfOneConstraintIsShownOnItsField()
    {
        Refuse = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.BadRequest, "FCMS2311", "grants[1].constraints.locales") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was not saved: the server refused the grants marked."));
        var row = Row(app, "content.article.read");
        row.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The server refused this grant.");
        Group(row, "Locales").QuerySelector(".cms-field-message-error")!.TextContent.ShouldBe("The server refused this limit.");
        Group(row, "Fields").QuerySelectorAll(".cms-field-message-error").ShouldBeEmpty();
        RuleSelect(row).HasAttribute("aria-invalid").ShouldBeFalse();
        Group(row, "Locales").QuerySelectorAll("fluent-checkbox").ShouldAllBe(box => box.GetAttribute("aria-invalid") == "true");
    }

    // Only the member the user changed is written: the sites, locales, row rule and resources go back as read, and the
    // media folder of a media grant is named by GET /media/folders.
    [Fact]
    public void AGrantLimitedToResourcesKeepsThemThroughAnEditOfItsOtherConstraints()
    {
        var read = RoleOf(Reviewers)["grants"]!.DeepClone().AsArray();
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "media.read").QuerySelector("dl")!.TextContent.ShouldContain("Press kit"));
        Row(app, "media.read").TextContent.ShouldNotContain(PressKit);
        Row(app, "content.article.read").QuerySelector("dl")!.TextContent.ShouldContain("folder-1, folder-2");
        Row(app, "content.article.read").TextContent.ShouldContain("The resources this grant is limited to are kept as they are; they are not changed here.");

        Uncheck(app, "content.article.read", "Fields", "body");
        Check(app, "media.read", "Locales", "fr");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var expected = read.DeepClone().AsArray();
        expected[0]!.AsObject().Remove("constraints");
        expected[1]!["constraints"]!["fields"] = new JsonArray("title");
        expected[2]!["constraints"]!["locales"] = new JsonArray("fr");
        JsonNode.DeepEquals(LastWrite(HttpMethod.Put)["grants"], expected).ShouldBeTrue(LastWrite(HttpMethod.Put)["grants"]!.ToJsonString());
        Management.Count("media/folders").ShouldBe(1);
    }

    // A list with nothing checked does not narrow, so it is left out; constraints with no member left are none.
    [Fact]
    public void ALimitThatNoLongerNarrowsIsLeftOutOfTheRequest()
    {
        RoleOf(Reviewers)["grants"]!.AsArray().Add(Grant("content.article.create", new JsonObject { ["locales"] = new JsonArray("fr") }));
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.create"));

        Uncheck(app, "content.article.read", "Fields", "title");
        Uncheck(app, "content.article.read", "Fields", "body");
        Uncheck(app, "content.article.create", "Locales", "fr");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var grants = LastWrite(HttpMethod.Put)["grants"]!.AsArray();
        grants[1]!["constraints"]!.AsObject().ContainsKey("fields").ShouldBeFalse();
        grants[1]!["constraints"]!["resources"].ShouldNotBeNull();
        JsonNode.DeepEquals(grants[3], new JsonObject { ["permission"] = "content.article.create" }).ShouldBeTrue(grants[3]!.ToJsonString());
    }

    // A value stored that is not offered, such as a site of another role's reach, is shown so and can be taken off.
    [Fact]
    public void AStoredValueThatIsNotOfferedIsShownAndCanBeTakenOff()
    {
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        Labels(Group(Row(app, "content.article.read"), "Sites")).ShouldBe(["Main (not offered here)"]);
        Uncheck(app, "content.article.read", "Sites", "Main (not offered here)");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        LastWrite(HttpMethod.Put)["grants"]![1]!["constraints"]!.AsObject().ContainsKey("sites").ShouldBeFalse();
    }

    // The rules could not be read: none is offered but the one stored, and the editor says so.
    [Fact]
    public void WithoutTheRowRulesNoneIsOfferedButTheOneStored()
    {
        var respond = Management.Respond!;
        Management.Respond = (request, path, body) => path == "permissions/row-rules" ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : respond(request, path, body);
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        var row = Row(app, "content.article.read");
        Options(RuleSelect(row)).ShouldBe(["No row rule", "own-entries (not registered for this grant)"]);
        row.TextContent.ShouldContain("The registered row rules could not be read, so none is offered.");
    }

    // The duplicate check compares identities, so constraints read as an empty object are none.
    [Fact]
    public void AGrantWhoseConstraintsAreAnEmptyObjectIsNotAddedTwice()
    {
        RoleOf(Reviewers)["grants"]![0]!["constraints"] = new JsonObject();
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "schema.read"));

        Add(app, GrantCatalogue.Core, "schema.read");

        app.WaitForAssertion(() => app.Find("main form").TextContent.ShouldContain("The role already grants schema.read."));
        GrantsShown(app).Count(grant => grant == "schema.read").ShouldBe(1);
    }

    // A member of the constraints the editor does not name goes back as read through an edit of another.
    [Fact]
    public void AMemberTheEditorDoesNotKnowIsSentBackThroughAnEdit()
    {
        RoleOf(Reviewers)["grants"]![1]!["constraints"]!["tenants"] = new JsonArray("north");
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        Uncheck(app, "content.article.read", "Fields", "body");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var sent = LastWrite(HttpMethod.Put)["grants"]![1]!["constraints"]!;
        JsonNode.DeepEquals(sent["tenants"], new JsonArray("north")).ShouldBeTrue(sent.ToJsonString());
        JsonNode.DeepEquals(sent["fields"], new JsonArray("title")).ShouldBeTrue(sent.ToJsonString());
    }

    // The refusal is of the rule that was sent: changing another limit keeps it, and another rule takes it off.
    [Fact]
    public void ARefusalOfARowRuleGoesOnceTheRuleIsChanged()
    {
        RoleOf(Reviewers)["grants"]![1]!["constraints"]!["rowRule"] = "retired";
        Refuse = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.BadRequest, "FCMS3042", "grants[1].constraints.rowRule") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));
        Save(app);
        app.WaitForAssertion(() => RuleSelect(Row(app, "content.article.read")).GetAttribute("aria-invalid").ShouldBe("true"));

        Uncheck(app, "content.article.read", "Fields", "body");
        RuleSelect(Row(app, "content.article.read")).GetAttribute("aria-invalid").ShouldBe("true");

        RuleSelect(Row(app, "content.article.read")).Change("own-entries");

        app.WaitForAssertion(() => RuleSelect(Row(app, "content.article.read")).HasAttribute("aria-invalid").ShouldBeFalse());
        Row(app, "content.article.read").TextContent.ShouldNotContain("There is no registered row rule");
        Row(app, "content.article.read").QuerySelector("[role=alert]").ShouldBeNull();
    }

    // A refusal opens the editor of the limits, which stays open as the user clears the refusal, until the next save.
    [Fact]
    public void TheLimitsARefusalOpenedStayOpenUntilTheNextSave()
    {
        var refusals = 1;
        Refuse = (method, path) => method == HttpMethod.Put && refusals-- > 0 ? Problem(HttpStatusCode.BadRequest, "FCMS2311", "grants[1].constraints.locales") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));
        Save(app);
        app.WaitForAssertion(() => Row(app, "content.article.read").QuerySelector("details")!.HasAttribute("open").ShouldBeTrue());

        Uncheck(app, "content.article.read", "Locales", "fr");

        Group(Row(app, "content.article.read"), "Locales").QuerySelectorAll(".cms-field-message-error").ShouldBeEmpty();
        Row(app, "content.article.read").QuerySelector("details")!.HasAttribute("open").ShouldBeTrue();

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        Row(app, "content.article.read").QuerySelector("details")!.HasAttribute("open").ShouldBeFalse();
    }

    // Each grant has the same limits, so a screen reader hears which grant a group or the rule belongs to.
    [Fact]
    public void TheLimitsNameTheirGrantToAScreenReader()
    {
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        var row = Row(app, "content.article.read");
        foreach (var legend in Groups(row))
        {
            Group(row, legend).QuerySelector("legend .cms-visually-hidden")!.TextContent.Trim().ShouldBe("of the grant content.article.read");
        }

        var described = RuleSelect(row).GetAttribute("aria-describedby")!.Split(' ');
        described.Select(id => row.QuerySelector($"#{id}")?.TextContent.Trim()).ShouldContain("of the grant content.article.read");
    }

    // The answer of a save replaces the grants, so nothing of them changes while it is awaited.
    [Fact]
    public void TheGrantsCannotBeChangedWhileASaveIsInFlight()
    {
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.EndsWith($"roles/{Reviewers:D}", StringComparison.Ordinal) ? held.Task : null;

        Save(app);

        app.WaitForAssertion(() => RuleSelect(Row(app, "content.article.read")).HasAttribute("disabled").ShouldBeTrue());
        Boxes(Group(Row(app, "content.article.read"), "Fields")).ShouldAllBe(box => box.HasAttribute("disabled"));
        Button(app, "Remove the grant schema.read").HasAttribute("disabled").ShouldBeTrue();
        Field(app, "Permission or pattern").HasAttribute("disabled").ShouldBeTrue();
        Field(app, "Name").HasAttribute("disabled").ShouldBeTrue();
        Field(app, "Description").HasAttribute("disabled").ShouldBeTrue();

        held.SetResult();

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        Boxes(Group(Row(app, "content.article.read"), "Fields")).ShouldAllBe(box => !box.HasAttribute("disabled"));
    }

    // The server keeps a site or a field given twice; the editor shows it once.
    [Fact]
    public void AValueStoredTwiceIsOfferedOnce()
    {
        RoleOf(Reviewers)["grants"]![1]!["constraints"]!["fields"] = new JsonArray("title", "title", "legacy", "legacy");
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        Labels(Group(Row(app, "content.article.read"), "Fields")).ShouldBe(["title", "body", "summary", "legacy (not offered here)"]);
        Checked(Group(Row(app, "content.article.read"), "Fields")).ShouldBe(["title", "legacy (not offered here)"]);
    }

    // FCMS3045 after the user narrowed one grant and limited another: the other change limited the first too, so its
    // version stays and the message names it; the user's change of the second, which the other change left, is kept.
    [Fact]
    public void AConcurrentChangeOfAGrantTheUserNarrowedKeepsTheOtherChangeAndSaysSo()
    {
        var refusals = 1;
        Refuse = (method, path) => method == HttpMethod.Put && refusals-- > 0 ? Problem(HttpStatusCode.Conflict, "FCMS3045", "id") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Row(app, "content.article.read"));

        Uncheck(app, "content.article.read", "Fields", "body");
        Check(app, "media.read", "Locales", "fr");
        RoleOf(Reviewers)["grants"]![1]!["constraints"]!["locales"] = new JsonArray("fr");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe(
            "Someone saved a change of this role while you edited it. The role was read again, with your changes kept on it, except your change to the limits of content.article.read, which the other change also changed or took away: check those grants, change them again if you still want to, and save again."));
        Checked(Group(Row(app, "content.article.read"), "Fields")).ShouldBe(["title", "body"]);
        Checked(Group(Row(app, "content.article.read"), "Locales")).Select(label => label[..2]).ShouldBe(["fr"]);
        Checked(Group(Row(app, "media.read"), "Locales")).Select(label => label[..2]).ShouldBe(["fr"]);

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var grants = LastWrite(HttpMethod.Put)["grants"]!;
        JsonNode.DeepEquals(grants[1]!["constraints"], RoleOf(Reviewers)["grants"]![1]!["constraints"]).ShouldBeTrue(grants.ToJsonString());
        JsonNode.DeepEquals(grants[1]!["constraints"]!["locales"], new JsonArray("fr")).ShouldBeTrue(grants.ToJsonString());
        JsonNode.DeepEquals(grants[2]!["constraints"], new JsonObject { ["resources"] = new JsonArray(PressKit), ["locales"] = new JsonArray("fr") }).ShouldBeTrue(grants.ToJsonString());
    }

    private static readonly Guid ShopEditors2 =new("0193a1b2-0000-7000-8000-0000000000b4");

    private static IElement Row(IRenderedComponent<AdminApp> app, string permission) => app.Find($"main [data-grant='{permission}']");

    private static string[] Groups(IElement row) => [.. row.QuerySelectorAll("fieldset legend").Select(Shown)];

    private static IElement Group(IElement row, string legend) =>
        row.QuerySelectorAll("fieldset").Single(group => Shown(group.QuerySelector("legend")!) == legend);

    // The text of a legend that is shown, without the grant's name that only a screen reader reads.
    private static string Shown(IElement legend) =>
        legend.TextContent.Replace(legend.QuerySelector(".cms-visually-hidden")?.TextContent ?? string.Empty, string.Empty, StringComparison.Ordinal).Trim();

    private static IElement RuleSelect(IElement row) => row.QuerySelector("details select")!;

    private static IEnumerable<IElement> Boxes(IElement group) => group.QuerySelectorAll("fluent-checkbox");

    // The wrapped set puts a checkbox's label beside it, in the field that holds both.
    private static string LabelOf(IElement box) => box.ParentElement!.QuerySelector("label")!.TextContent.Trim();

    private static string[] Labels(IElement group) => [.. Boxes(group).Select(LabelOf)];

    private static string[] Checked(IElement group) => [.. Boxes(group).Where(box => box.HasAttribute("checked")).Select(LabelOf)];

    private static void Check(IRenderedComponent<AdminApp> app, string permission, string legend, string label) => Set(app, permission, legend, label, true);

    private static void Uncheck(IRenderedComponent<AdminApp> app, string permission, string legend, string label) => Set(app, permission, legend, label, false);

    private static void Set(IRenderedComponent<AdminApp> app, string permission, string legend, string label, bool on)
    {
        Boxes(Group(Row(app, permission), legend)).Single(box => LabelOf(box).StartsWith(label, StringComparison.Ordinal)).Change(on);

        // A value taken off that is not offered goes with its box, and its group when it was the last.
        app.WaitForAssertion(() => Row(app, permission).QuerySelectorAll("fieldset")
            .Where(group => Shown(group.QuerySelector("legend")!) == legend)
            .SelectMany(Checked)
            .Any(shown => shown.StartsWith(label, StringComparison.Ordinal))
            .ShouldBe(on));
    }
}
