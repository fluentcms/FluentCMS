using System.Reflection;
using System.Text.Json;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Careless;
using FluentCMS.Admin.Plugins.Sample;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Plugins;

// The manifest of an admin half and the helper that holds it to the attributes of the half's assembly, against the sample half.
public sealed class AdminPluginManifestTests
{
    private const string Sample = "FluentCMS.Admin.Plugins.Sample.";

    private static readonly Assembly Half = typeof(ReviewsAdminPlugin).Assembly;

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName);

    private static AdminPluginManifest File => AdminPluginManifestJson.Read(System.IO.File.ReadAllText(ManifestPath));

    private static AdminPluginManifest Declared => AdminPluginManifestCheck.FromAssembly(Half);

    [Fact]
    public void TheSampleHalfAgreesWithItsManifest()
    {
        AdminPluginManifestCheck.Differences(Half, System.IO.File.ReadAllText(ManifestPath)).ShouldBeEmpty();
        Should.NotThrow(() => AdminPluginManifestCheck.VerifyFile(Half, ManifestPath));
    }

    [Fact]
    public void TheAttributesOfAnAssemblyDeclareEveryContribution()
    {
        var manifest = Declared;

        manifest.Name.ShouldBe("reviews");
        manifest.Version.ShouldBe("1.0.0");
        manifest.AdminRange.ShouldBe("[1.0.0,2.0.0)");
        manifest.Assembly.ShouldBe("FluentCMS.Admin.Plugins.Sample");
        manifest.Pages.Select(page => (page.Type, page.Route, page.Permission)).ShouldBe(
        [
            (Sample + "ReviewPage", "/reviews/{Id:guid}", "reviews.notes.manage"),
            (Sample + "ReviewPage", "/reviews/new", "reviews.notes.create"),
            (Sample + "ReviewsPage", "/reviews", "reviews.notes.manage"),
            (Sample + "MisinjectedPage", "/reviews/misinjected", "reviews.notes.manage"),
        ], ignoreOrder: true);
        manifest.MenuItems.ShouldHaveSingleItem().ShouldBe(new AdminManifestMenuItem
        {
            Type = Sample + "ReviewsPage",
            Label = "Reviews.Menu",
            Route = "/reviews",
            Section = AdminSection.Content,
            Icon = "comment",
            Order = 10,
            Permission = "reviews.notes.manage",
        });
        manifest.SettingsPages.ShouldHaveSingleItem().Area.ShouldBe("reviews");
        manifest.FieldEditors.ShouldHaveSingleItem().ShouldBe(new AdminManifestFieldComponent { Type = Sample + "RatingEditor", FieldType = "rating" });
        manifest.ListCells.ShouldHaveSingleItem().ShouldBe(new AdminManifestFieldComponent { Type = Sample + "RatingCell", FieldType = "rating" });
        manifest.EntryActions.ShouldHaveSingleItem().ContentTypes.ShouldBe(["review"]);
        manifest.BulkActions.ShouldHaveSingleItem().Name.ShouldBe("approveAll");
        manifest.FieldActions.ShouldHaveSingleItem().FieldTypes.ShouldBe(["text", "longText"]);
        manifest.Widgets.ShouldHaveSingleItem().ShouldBe(new AdminManifestWidget
        {
            Type = Sample + "LatestReviewsWidget",
            Title = "Reviews.Widget.Title",
            Size = WidgetSize.Small,
            Permission = "reviews.notes.manage",
            Role = "editor",
        });
        manifest.Services.ShouldHaveSingleItem().Service.ShouldBe("FluentCMS.Admin.Pickers.IMediaPicker");
    }

    [Fact]
    public void AManifestIsWrittenAndReadBack()
    {
        var json = AdminPluginManifestJson.Write(Labelled(Declared));

        json.ShouldContain("\"manifestVersion\": 1");
        json.ShouldContain("\"section\": \"content\"");
        json.ShouldContain("\"size\": \"small\"");
        json.ShouldNotContain("null");
        AdminPluginManifestCheck.Compare(AdminPluginManifestJson.Read(json), Declared).ShouldBeEmpty();
    }

    [Fact]
    public void AListOfManifestsIsWrittenAndReadBack()
    {
        IReadOnlyList<AdminPluginManifest> manifests = [Declared, Declared with { Name = "media" }];

        var read = JsonSerializer.Deserialize(JsonSerializer.Serialize(manifests, AdminPluginManifestJson.Manifests), AdminPluginManifestJson.Manifests);

        read.ShouldNotBeNull().Select(manifest => manifest.Name).ShouldBe(["reviews", "media"]);
        read[1].Widgets.ShouldHaveSingleItem().Size.ShouldBe(WidgetSize.Small);
    }

    [Fact]
    public void AHandWrittenFileMayHoldCommentsTrailingCommasAndMembersOfALaterVersion()
    {
        var manifest = AdminPluginManifestJson.Read("""
            {
              // The media half.
              "name": "media",
              "assembly": "FluentCMS.Plugins.Media.Admin",
              "later": { "any": true },
              "menuItems": [
                { "type": "Media.MediaPage", "label": "Media.Menu", "route": "/media", "section": "Media", "later": 1, },
              ],
            }
            """);

        manifest.ManifestVersion.ShouldBe(1);
        manifest.Pages.ShouldBeEmpty();
        manifest.Services.ShouldBeEmpty();
        manifest.MenuItems.ShouldHaveSingleItem().Section.ShouldBe(AdminSection.Media);
    }

    [Theory]
    [InlineData("""{ "assembly": "Reviews.Admin" }""")]
    [InlineData("null")]
    public void ATextThatIsNoManifestIsRefused(string json) =>
        Should.Throw<JsonException>(() => AdminPluginManifestJson.Read(json));

    [Fact]
    public void ASectionOrASizeOfALaterVersionDoesNotStopTheRead()
    {
        var manifests = JsonSerializer.Deserialize("""
            [
              {
                "manifestVersion": 2,
                "name": "later",
                "assembly": "Later.Admin",
                "menuItems": [{ "type": "P", "label": "L", "route": "/later", "section": "tools" }],
                "widgets": [{ "type": "W", "title": "T", "size": 4 }, { "type": "V", "title": "U", "size": "huge" }]
              },
              { "name": "media", "assembly": "Media.Admin", "widgets": [{ "type": "W", "title": "T", "size": "wide" }] }
            ]
            """, AdminPluginManifestJson.Manifests).ShouldNotBeNull();

        manifests.Select(manifest => (manifest.Name, manifest.IsValid)).ShouldBe([("later", false), ("media", true)]);
        manifests[0].ManifestVersion.ShouldBe(2);
        Enum.IsDefined(manifests[0].MenuItems.ShouldHaveSingleItem().Section).ShouldBeFalse();
        manifests[0].Widgets.ShouldAllBe(widget => !Enum.IsDefined(widget.Size));
        manifests[1].Widgets.ShouldHaveSingleItem().Size.ShouldBe(WidgetSize.Wide);
        JsonSerializer.Serialize(manifests, AdminPluginManifestJson.Manifests).ShouldContain("\"section\": \"unknown\"");
    }

    [Fact]
    public void AVersionOfZeroAndAnUnknownSectionOrSizeInTheFileOfAHalfAreNamed()
    {
        var json = System.IO.File.ReadAllText(ManifestPath)
            .Replace("\"manifestVersion\": 1", "\"manifestVersion\": 0", StringComparison.Ordinal)
            .Replace("\"section\": \"content\"", "\"section\": \"contnet\"", StringComparison.Ordinal)
            .Replace("\"size\": \"small\"", "\"size\": 1", StringComparison.Ordinal);

        AdminPluginManifestCheck.Differences(Half, json).ShouldBe(
        [
            "manifestVersion is 0 in the manifest and this contract reads 1",
            $"menu item 'Reviews.Menu' of {Sample}ReviewsPage: section is 'content' in the assembly and 'unknown' in the manifest",
            $"widget 'Reviews.Widget.Title' of {Sample}LatestReviewsWidget: size is 'small' in the assembly and 'unknown' in the manifest",
        ]);
    }

    [Fact]
    public void AContributionMissingFromTheManifestIsNamed()
    {
        var file = File with { Widgets = [], ListCells = [] };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBe(
        [
            $"list cell 'rating' of {Sample}RatingCell: declared by the assembly and missing from the manifest",
            $"widget 'Reviews.Widget.Title' of {Sample}LatestReviewsWidget: declared by the assembly and missing from the manifest",
        ]);
    }

    [Fact]
    public void AContributionMissingFromTheAssemblyIsNamed()
    {
        var file = File with
        {
            FieldEditors = [.. File.FieldEditors, new AdminManifestFieldComponent { Type = Sample + "StarsEditor", FieldType = "stars" }],
            Pages = [.. File.Pages, new AdminManifestPage { Type = Sample + "ReviewsPage", Route = "/reviews/archive" }],
        };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBe(
        [
            $"page '/reviews/archive' of {Sample}ReviewsPage: in the manifest and not declared by the assembly",
            $"field editor 'stars' of {Sample}StarsEditor: in the manifest and not declared by the assembly",
        ]);
    }

    [Fact]
    public void ADifferentRouteFieldTypeOrPermissionIsNamed()
    {
        var file = File with
        {
            Pages = [.. File.Pages.Select(page => page.Route switch
            {
                "/reviews" => page with { Route = "/review" },
                "/reviews/new" => page with { Permission = null },
                _ => page,
            })],
            FieldEditors = [File.FieldEditors[0] with { FieldType = "stars" }],
            EntryActions = [File.EntryActions[0] with { Permission = "reviews.approve", ContentTypes = ["review", "note"] }],
        };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBe(
        [
            $"page '/reviews/new' of {Sample}ReviewPage: permission is 'reviews.notes.create' in the assembly and not set in the manifest",
            $"entry action 'approve' of {Sample}ApproveAction: contentTypes is 'review' in the assembly and 'note, review' in the manifest",
            $"entry action 'approve' of {Sample}ApproveAction: permission is 'reviews.notes.manage' in the assembly and 'reviews.approve' in the manifest",
            $"page of {Sample}ReviewsPage: route is '/reviews' in the assembly and '/review' in the manifest",
            $"field editor of {Sample}RatingEditor: fieldType is 'rating' in the assembly and 'stars' in the manifest",
        ]);
    }

    // The form reads FieldsFrom before the half loads, so the file must say what the attribute says.
    [Fact]
    public void ADifferentFieldsFromOfAFieldActionIsNamed()
    {
        var file = File with { FieldActions = [File.FieldActions[0] with { FieldsFrom = "reviews/fields#fields" }] };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBe(
        [
            $"field action 'shout' of {Sample}ShoutAction: fieldsFrom is not set in the assembly and 'reviews/fields#fields' in the manifest",
        ]);
        AdminPluginManifestJson.Read(AdminPluginManifestJson.Write(file)).FieldActions[0].FieldsFrom.ShouldBe("reviews/fields#fields");
    }

    [Fact]
    public void ADifferentMemberOfTheManifestOrOfAMenuItemOrAWidgetIsNamed()
    {
        var file = File with
        {
            ManifestVersion = 2,
            Name = "review",
            AdminRange = null,
            MenuItems = [File.MenuItems[0] with { Section = AdminSection.Settings, Icon = null, Order = 0 }],
            Widgets = [File.Widgets[0] with { Size = WidgetSize.Wide, Role = "author" }],
        };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBe(
        [
            "manifestVersion is 2 in the manifest and this contract reads 1",
            "name is 'reviews' in the assembly and 'review' in the manifest",
            "adminRange is '[1.0.0,2.0.0)' in the assembly and not set in the manifest",
            $"menu item 'Reviews.Menu' of {Sample}ReviewsPage: section is 'content' in the assembly and 'settings' in the manifest",
            $"menu item 'Reviews.Menu' of {Sample}ReviewsPage: icon is 'comment' in the assembly and not set in the manifest",
            $"menu item 'Reviews.Menu' of {Sample}ReviewsPage: order is '10' in the assembly and '0' in the manifest",
            $"widget 'Reviews.Widget.Title' of {Sample}LatestReviewsWidget: size is 'small' in the assembly and 'wide' in the manifest",
            $"widget 'Reviews.Widget.Title' of {Sample}LatestReviewsWidget: role is 'editor' in the assembly and 'author' in the manifest",
        ]);
    }

    [Fact]
    public void TheOrderOfTheContributionsAndOfTheirTargetsDoesNotMatter()
    {
        var file = File with { Pages = [.. File.Pages.Reverse()], BulkActions = [File.BulkActions[0] with { ContentTypes = ["review", "note"] }] };

        AdminPluginManifestCheck.Compare(file, Declared).ShouldBeEmpty();
    }

    [Fact]
    public void VerifyFailsWithEveryDifferenceOnALineOfItsOwn()
    {
        var json = AdminPluginManifestJson.Write(File with { Version = "2.0.0", Services = [] });

        var failure = Should.Throw<InvalidOperationException>(() => AdminPluginManifestCheck.Verify(Half, json));

        failure.Message.Split(Environment.NewLine).ShouldBe(
        [
            "fluentcms-admin.json and the attributes of FluentCMS.Admin.Plugins.Sample disagree:",
            "version is '1.0.0' in the assembly and '2.0.0' in the manifest",
            $"service 'FluentCMS.Admin.Pickers.IMediaPicker' of {Sample}SampleMediaPicker: declared by the assembly and missing from the manifest",
        ]);
    }

    [Fact]
    public void AManifestThatCannotBeReadIsNamed()
    {
        var difference = AdminPluginManifestCheck.Differences(Half, """{ "name": "reviews" }""").ShouldHaveSingleItem();

        difference.ShouldStartWith("the manifest cannot be read: ");
        Should.Throw<InvalidOperationException>(() => AdminPluginManifestCheck.Verify(Half, "{"));
    }

    [Fact]
    public void AnAssemblyThatNamesNoPluginIsNamed()
    {
        var assembly = typeof(AdminPluginManifest).Assembly;

        AdminPluginManifestCheck.Differences(assembly, "{}").ShouldBe(
            ["FluentCMS.Admin.Abstractions names no plugin: add [assembly: FluentCmsAdminPlugin(typeof(...))]"]);
        Should.Throw<InvalidOperationException>(() => AdminPluginManifestCheck.FromAssembly(assembly));
    }

    // The careless half of the tests (FluentCMS.Admin.Plugins.Careless) declares what the admin could not use.
    [Fact]
    public void ADeclarationTheAdminCouldNotUseIsNamed()
    {
        var careless = typeof(CarelessPlugin).Assembly;
        const string Types = "FluentCMS.Admin.Plugins.Careless.";
        var json = AdminPluginManifestJson.Write(AdminPluginManifestCheck.FromAssembly(careless));

        AdminPluginManifestCheck.Differences(careless, json).ShouldBe(
        [
            "the plugin's name 'system' is a reserved first segment of the admin's routes",
            $"AdminRange 'soon' of {Types}CarelessPlugin is not a range of versions, such as [1.0.0,2.0.0)",
            $"{Types}CarelessPlugin does not implement IAdminPlugin",
            $"entry action 'careless' of {Types}CarelessAction: the class does not derive from EntryActionBase",
            $"bulk action 'careless' of {Types}CarelessAction: the class does not derive from BulkActionBase",
            $"field action 'careless' of {Types}CarelessAction: the class does not derive from FieldActionBase",
            $"field action 'careless' of {Types}CarelessAction: FieldsFrom '../ai/fields#fields' is not a read of the Management API and a member of its answer, such as ai/fields#fields",
            $"service 'System.IDisposable' of {Types}CarelessAction: the class does not implement it",
            $"menu item 'Careless.Menu' of {Types}CarelessMenuItem: the component declares no [AdminPage] for it to link to",
            $"field editor 'careless' of {Types}CarelessMenuItem: the class does not derive from FieldEditorBase",
            $"list cell 'careless' of {Types}CarelessMenuItem: the class does not derive from ListCellBase",
            $"page '/system/careless' of {Types}CarelessPage: the component has no @page of that route, so the router cannot find it",
            $"page '/system/careless/other' of {Types}CarelessPage: the component has no @page of that route, so the router cannot find it",
            $"page '/system/careless/other' of {Types}CarelessPage: InstanceOnly needs a Permission for the user to hold for the instance, and without one the shell shows the page to no one",
            $"menu item 'Careless.Pages' of {Types}CarelessPage: the component has several pages, so the item must name its Route",
            $"settings page 'careless' of {Types}CarelessPage: the class does not derive from IComponent",
            $"widget 'Careless.Widget' of {Types}CarelessPage: the class does not derive from IComponent",
            $"@page '/content/careless' of {Types}CarelessRoute: the component declares no [AdminPage] of that route, so the shell cannot gate it",
            $"@page '/content/careless' of {Types}CarelessRoute: the route is not /system or below it",
            $"page '/careless/item' of {Types}CarelessTwin: the component has no @page of that route, so the router cannot find it",
            $"page '/careless/item' of {Types}CarelessTwin: the route is not /system or below it",
            $"menu item 'Careless.Item' of {Types}CarelessTwin: the component declares no [AdminPage] of its Route '/careless/items'",
            $"field action 'twin' of {Types}CarelessTwin: the class does not derive from FieldActionBase",
            $"field action 'twin' of {Types}CarelessTwin: FieldsFrom 'file:x#fields' is not a read of the Management API and a member of its answer, such as ai/fields#fields",
            $"service 'System.IDisposable' of {Types}CarelessTwin: the class does not implement it",
            $"service 'System.IDisposable' is declared more than once: by {Types}CarelessAction and {Types}CarelessTwin",
        ]);
    }

    [Fact]
    public void ANullWhereTheManifestTakesNoneIsRefusedOrNamedAndNeverThrows()
    {
        var file = System.IO.File.ReadAllText(ManifestPath);
        var nullRoute = file.Replace("\"route\": \"/reviews\",", "\"route\": null,", StringComparison.Ordinal);
        var nullList = file.Replace("\"widgets\": [", "\"widgets\": null, \"later\": [", StringComparison.Ordinal);
        var nullEntry = file.Replace("\"pages\": [", "\"pages\": [null,", StringComparison.Ordinal);

        Should.Throw<JsonException>(() => AdminPluginManifestJson.Read(nullRoute));
        Should.Throw<JsonException>(() => AdminPluginManifestJson.Read(nullList));
        AdminPluginManifestCheck.Differences(Half, nullRoute).ShouldHaveSingleItem().ShouldStartWith("the manifest cannot be read: ");
        AdminPluginManifestJson.Read(nullEntry).IsValid.ShouldBeFalse();
        AdminPluginManifestCheck.Differences(Half, nullEntry).ShouldBe(["pages holds a null in the manifest"]);
    }

    [Fact]
    public void TheSampleHalfIsValid() => File.IsValid.ShouldBeTrue();

    // The texts of the menu items, which only the file has.
    private static AdminPluginManifest Labelled(AdminPluginManifest manifest) => manifest with
    {
        MenuItems = [.. manifest.MenuItems.Select(item => item with { Labels = new Dictionary<string, string> { ["en"] = item.Label } })],
    };
}
