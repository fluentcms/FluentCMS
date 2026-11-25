using System.Text.Json;
using Deque.AxeCore.Commons;
using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// axe-core on a page as it stands, with the rules of WCAG 2.0, 2.1 and 2.2 at levels A and AA (spec 4.18, 7). A violation
// is a line with its view, its rule, its target and the address of its help page, unless an exception of the one list
// (AccessibilityExceptions) covers it. Only violations are asked for: what axe-core cannot decide alone is not reported.
public static class Axe
{
    // axe-core has no rule of a criterion new at level A in 2.2, and so no tag wcag22a.
    public static readonly IReadOnlyList<string> Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    private static readonly string Script = new BundledAxeScriptProvider().GetScript();

    // The admin has no frame of its own: the preview pane's frame shows the front end's page.
    private const string Run = """
        async tags => {
            const result = await axe.run(document, { runOnly: { type: 'tag', values: tags }, resultTypes: ['violations'], iframes: false });
            return result.violations.flatMap(violation => violation.nodes.map(node => ({
                rule: violation.id,
                impact: violation.impact ?? '',
                help: violation.help,
                helpUrl: violation.helpUrl,
                target: node.target.map(part => Array.isArray(part) ? part.join(' >>> ') : part).join(' '),
                html: node.html,
            })));
        }
        """;

    public static async Task<IReadOnlyList<string>> ViolationsAsync(IPage page, string view)
    {
        // Once per document: the app's own navigation keeps the document, and axe-core with it.
        if (!await page.EvaluateAsync<bool>("() => typeof window.axe !== 'undefined'"))
        {
            await page.EvaluateAsync(Script);
        }

        var found = await page.EvaluateAsync<JsonElement>(Run, Tags);
        return [.. found.EnumerateArray()
            .Select(node => (Rule: Text(node, "rule"), Impact: Text(node, "impact"), Help: Text(node, "help"), HelpUrl: Text(node, "helpUrl"), Target: Text(node, "target"), Html: Text(node, "html")))
            .Where(node => !AccessibilityExceptions.Cover(view, node.Rule, node.Target))
            .Select(node => $"{view}: {node.Rule} ({node.Impact}) at {node.Target}: {node.Help}. {node.HelpUrl}{Environment.NewLine}    {node.Html}")];
    }

    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString() ?? "";
}
