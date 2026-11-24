using Bunit;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Shell;

// AdminContext.ReadPluginsAsync, which the plugins page calls after a plugin was enabled or disabled in the site shown
// (spec 4.15), so that the plugin's contributions follow without a reload.
public sealed class ReadPluginsTests : AdminTestContext
{
    public ReadPluginsTests() => UseNeutralResources();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    [Fact]
    public async Task ThePluginsAreReadAgainPastTheCacheAndTheChangeIsRaised()
    {
        Management.Plugins[MainSite] = [Plugin("search", enabled: true)];
        var app = Load();
        var changes = 0;
        Context.Changed += () => changes++;
        Management.Plugins[MainSite] = [Plugin("search", enabled: false)];

        await app.InvokeAsync(() => Context.ReadPluginsAsync());

        Context.PluginEnabled("search").ShouldBeFalse();
        changes.ShouldBe(1);
        Management.Count("plugins").ShouldBe(2);
    }

    // Two pages, or two quick switches, can read the plugins at once; the read that started last is what the server holds.
    [Fact]
    public async Task OfTwoReadsThatOverlapTheOneStartedLastAppliesWhicheverEndsLast()
    {
        Management.Plugins[MainSite] = [Plugin("search", enabled: true)];
        var app = Load();
        var held = new TaskCompletionSource();
        var reads = 0;
        Management.HoldPath = path => path == "/api/management/v1/plugins" && ++reads == 1 ? held.Task : null;
        Task older = Task.CompletedTask;
        await app.InvokeAsync(() => { older = Context.ReadPluginsAsync(); });

        Management.Plugins[MainSite] = [Plugin("search", enabled: false)];
        await app.InvokeAsync(() => Context.ReadPluginsAsync());
        Context.PluginEnabled("search").ShouldBeFalse();

        // The older read answers what the server held before, and ends last.
        Management.Plugins[MainSite] = [Plugin("search", enabled: true)];
        held.SetResult();
        await app.InvokeAsync(() => older);

        Context.PluginEnabled("search").ShouldBeFalse();
        Management.Count("plugins").ShouldBe(3);
    }

    [Fact]
    public async Task AReadThatFailsLeavesThePluginsAsTheyWere()
    {
        Management.Plugins[MainSite] = [Plugin("search", enabled: true)];
        var app = Load();
        Management.Unavailable = true;

        await app.InvokeAsync(() => Context.ReadPluginsAsync());

        Context.PluginEnabled("search").ShouldBeTrue();
        Context.Status.ShouldBe(ShellStatus.Ready);
    }

    private IRenderedComponent<AdminApp> Load()
    {
        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo("");
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        return app;
    }
}
