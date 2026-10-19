using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

// UX-04: the admin targets WCAG 2.1 AA, checked with axe-core on the showcase and the desk's pages in both directions and themes.
public sealed class AccessibilityTests
{
    private static readonly AxeRunOptions Wcag21AA = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
    };

    [BrowserFact]
    public async Task The_showcase_desk_and_set_view_have_no_wcag_21_aa_violations()
    {
        await using var session = await BrowserSession.StartAsync();
        var set = await AwaitingReviewAsync(session);
        await session.SignInWithKeyboardAsync();

        string[] paths = ["/ui", "/", "/?queue=stale-facts", $"/sets/{set.Id}?queue=awaiting-me", "/security"];
        var violations = new List<string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            await session.Page.Context.AddCookiesAsync([new Cookie { Name = "fluentcms-theme", Value = theme, Url = session.BaseUrl }]);
            foreach (var path in paths)
            {
                foreach (var direction in new[] { "ltr", "rtl" })
                {
                    var address = $"{session.BaseUrl}{path}{(path.Contains('?') ? '&' : '?')}dir={direction}";
                    await session.Page.GotoAsync(address);
                    await session.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    var result = await session.Page.RunAxe(Wcag21AA);
                    violations.AddRange(result.Violations.Select(violation =>
                        $"{theme} {path} {direction}: {violation.Id} ({violation.Impact}) {violation.Help}: "
                        + string.Join(" | ", violation.Nodes.Take(3).Select(node => node.Target + " " + node.Html))));
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static async Task<ChangeSet> AwaitingReviewAsync(BrowserSession session)
    {
        var type = await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory =>
            (await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])))).Value);

        return await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet("Add the product type.", ["https://example.com/spec"]))).Value;
            await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value;
        });
    }
}
