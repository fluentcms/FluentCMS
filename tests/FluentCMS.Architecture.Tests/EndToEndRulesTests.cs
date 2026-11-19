using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluentCMS.Architecture.Tests;

// The rules of the admin's browser tests (spec 7, CONTRIBUTING.md): a test class waits on the page's state through the shared
// helpers of the end-to-end project, never by a timer, and reads an answer by its request, status and headers, never by a body
// read after the fact, which Chromium may no longer hold. Each failure class these rules name has turned CI red on a slow
// runner. The rules run on the test classes of the repository and on a class that breaks each of them.
public class EndToEndRulesTests
{
    private const string Folder = "admin/tests/FluentCMS.Admin.EndToEnd";

    private static readonly Regex TestClass = new(@":\s*BrowserTest\s*\(", RegexOptions.CultureInvariant);

    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("reads a response's body (TextAsync, JsonAsync or BodyAsync); read the request, the status and the headers", new(@"\.(TextAsync|JsonAsync|BodyAsync)\s*(<[^>()]*>)?\s*\(", RegexOptions.CultureInvariant)),
        ("waits by a fixed delay; wait on the page's state, or use Ui.UntilAsync or Ui.FocusReturnedAsync", new(@"\b(WaitForTimeoutAsync|Task\s*\.\s*Delay|Thread\s*\.\s*Sleep|setTimeout)\s*\(", RegexOptions.CultureInvariant)),
        ("declares its own PressAsync, FocusAsync or FocusReturnedAsync; use the one of Ui", new(@"\bTask(<[^>]*>)?\s+(PressAsync|FocusAsync|FocusReturnedAsync)\s*\(", RegexOptions.CultureInvariant)),
    ];

    [Fact]
    public void TheEndToEndTestClassesUseTheSharedHelpersAndNoTimers()
    {
        var root = ProjectFile.FindRepositoryRoot();
        var classes = Directory.EnumerateFiles(Path.Combine(root, Folder), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => (Name: Path.GetFileName(path), Source: File.ReadAllText(path)))
            .Where(file => TestClass.IsMatch(file.Source))
            .ToList();

        // The rules see the classes they hold: every class of browser tests is one, so there are many.
        classes.Count.ShouldBeGreaterThan(10);
        classes.SelectMany(file => Violations(file.Name, file.Source)).ShouldBeEmpty();
    }

    [Fact]
    public void AClassThatBreaksEachRuleIsFound()
    {
        const string careless = """
            public sealed class CarelessTests(EndToEndHost host) : BrowserTest(host)
            {
                public async Task ReadsAndSleeps(IResponse response, IPage page)
                {
                    var body = await response.TextAsync();
                    var json = await response.JsonAsync<JsonObject>();
                    var bytes = await response.BodyAsync();
                    await page.WaitForTimeoutAsync(500);
                    await Task.Delay(100);
                    Thread.Sleep(100);
                    await page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve, 50))");
                    // A comment may name setTimeout( or TextAsync( without breaking a rule.
                    var text = await page.Locator("p").InnerTextAsync();
                }

                private static async Task PressAsync(ILocator target, string key) => await target.PressAsync(key);

                private static Task FocusReturnedAsync(ILocator opener) => Task.CompletedTask;
            }
            """;

        var found = Violations("CarelessTests.cs", careless).ToList();

        found.Count(violation => violation.Contains("body", StringComparison.Ordinal)).ShouldBe(3);
        found.Count(violation => violation.Contains("fixed delay", StringComparison.Ordinal)).ShouldBe(4);
        found.Count(violation => violation.Contains("its own", StringComparison.Ordinal)).ShouldBe(2);
        found.Count.ShouldBe(9);
    }

    // Each line that breaks a rule, but the comments, which may name what they explain.
    private static IEnumerable<string> Violations(string file, string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (rule, pattern) in Rules)
            {
                if (pattern.IsMatch(lines[index]))
                {
                    yield return $"{Folder}/{file}:{index + 1} {rule}: {lines[index].Trim()}";
                }
            }
        }
    }
}
