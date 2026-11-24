using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// axe-core on a page as it stands, with the rules of WCAG 2.0, 2.1 and 2.2 at levels A and AA (spec 4.18, 7). A violation
// is a line with its view, its rule, its target and the address of its help page, unless an exception of the one list
// (AccessibilityExceptions) covers it.
public static class Axe
{
    // axe-core has no rule of a criterion new at level A in 2.2, and so no tag wcag22a.
    public static readonly IReadOnlyList<string> Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    private static readonly AxeRunOptions Options = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] },
        ResultTypes = [ResultType.Violations],
    };

    public static async Task<IReadOnlyList<string>> ViolationsAsync(IPage page, string view)
    {
        var result = await page.RunAxe(Options);
        return [.. result.Violations
            .SelectMany(violation => violation.Nodes.Select(node => (Violation: violation, Target: node.Target.ToString(), node.Html)))
            .Where(found => !AccessibilityExceptions.Cover(view, found.Violation.Id, found.Target))
            .Select(found => $"{view}: {found.Violation.Id} ({found.Violation.Impact}) at {found.Target}: {found.Violation.Help}. {found.Violation.HelpUrl}{Environment.NewLine}    {found.Html}")];
    }
}
