using Bunit;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Shell;

// AdminContext.ReadAccessAsync, which the user's page calls after a change of the caller's own roles (spec 4.15), so that
// the gates and the menu follow what the user may now do without a reload.
public sealed class ReadAccessTests : AdminTestContext
{
    public ReadAccessTests() => UseNeutralResources();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    [Fact]
    public async Task TheAccessAndTheSchemaAreReadAgainPastTheCacheAndTheChangeIsRaised()
    {
        var app = Load();
        var changes = 0;
        Context.Changed += () => changes++;
        Management.Grant(MainSite, ["users.manage"]);

        await app.InvokeAsync(() => Context.ReadAccessAsync());

        Context.Allows("users.manage").ShouldBeTrue();
        Context.Allows("schema.read").ShouldBeFalse();
        Context.SchemaRefused.ShouldBeTrue();
        Context.Schema.ShouldBeNull();
        Context.AccessCurrent.ShouldBeTrue();
        changes.ShouldBe(1);
        Management.Count("me/access").ShouldBe(2);
    }

    // The read that started last is what the server holds, whichever ends last.
    [Fact]
    public async Task OfTwoReadsThatOverlapTheOneStartedLastApplies()
    {
        var app = Load();
        var held = new TaskCompletionSource();
        var reads = 0;
        Management.HoldPath = path => path == "/api/management/v1/me/access" && ++reads == 1 ? held.Task : null;
        Task older = Task.CompletedTask;
        await app.InvokeAsync(() => { older = Context.ReadAccessAsync(); });

        Management.Grant(MainSite, ["schema.read"]);
        await app.InvokeAsync(() => Context.ReadAccessAsync());
        Context.Allows("users.manage").ShouldBeFalse();

        // The older read answers what the server held before, and ends last.
        Management.Grant(MainSite, ["schema.read", "users.manage"]);
        held.SetResult();
        await app.InvokeAsync(() => older);

        Context.Allows("users.manage").ShouldBeFalse();
    }

    [Fact]
    public async Task AReadThatFailsLeavesTheAccessAsItWas()
    {
        var app = Load();
        Management.Unavailable = true;

        await app.InvokeAsync(() => Context.ReadAccessAsync());

        Context.Allows("users.manage").ShouldBeTrue();
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
