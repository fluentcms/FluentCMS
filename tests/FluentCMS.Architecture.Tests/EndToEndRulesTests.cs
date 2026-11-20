using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace FluentCMS.Architecture.Tests;

// The rules of the admin's browser tests (spec 7, CONTRIBUTING.md): a test waits on the page's state through the shared
// helpers of the end-to-end project, never by a timer or a clock of its own, and reads an answer by its request, status and
// headers, never by a body read after the fact, which Chromium may no longer hold. Each failure class these rules name has
// turned CI red on a slow runner. They hold every file of the project but the harness's own, named below, and they read the
// syntax, so a name is found however it is written: on two lines, as a method group, through `using static` or in a lambda.
// They also run on a file that breaks each of them.
public class EndToEndRulesTests
{
    private const string Folder = "admin/tests/FluentCMS.Admin.EndToEnd";

    // The harness, which waits by timers and reads clocks on purpose: the shared helpers, the browser context with its slow
    // mode and its tokens' due time, the host with its process, which wait for a process to start and to end, the browser's
    // launch, which times itself, and the run's certificates, whose validity starts now.
    private static readonly string[] Harness = ["Ui.cs", "AdminBrowser.cs", "SlowMode.cs", "EndToEndHost.cs", "ChildProcess.cs", "Chromium.cs", "TestCertificates.cs"];

    private const string Body = "reads a response's body after the fact; read its request, status and headers";
    private const string Timer = "waits by a timer; wait on the page's state, or use Ui.UntilAsync or Ui.FocusReturnedAsync";
    private const string Clock = "reads a clock of its own, as a hand-rolled poll does, which spins requests; use Ui.UntilAsync, or wait on the timed event (tab.TokenDueAsync)";
    private const string Copy = "declares its own PressAsync, FocusAsync or FocusReturnedAsync; use the one of Ui";

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["TextAsync"] = Body,
        ["JsonAsync"] = Body,
        ["BodyAsync"] = Body,
        ["WaitForTimeoutAsync"] = Timer,
        ["Delay"] = Timer,
        ["Sleep"] = Timer,
        ["UtcNow"] = Clock,
        ["Now"] = Clock,
        ["Stopwatch"] = Clock,
        ["TimeProvider"] = Clock,
        ["TickCount"] = Clock,
        ["TickCount64"] = Clock,
        ["GetTimestamp"] = Clock,
    };

    private static readonly string[] Copies = ["PressAsync", "FocusAsync", "FocusReturnedAsync"];

    [Fact]
    public void TheEndToEndTestsUseTheSharedHelpersAndNoTimersOrClocks()
    {
        var root = Path.Combine(ProjectFile.FindRepositoryRoot(), Folder);
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => (Name: Path.GetRelativePath(root, path).Replace('\\', '/'), Source: File.ReadAllText(path)))
            .ToList();

        // The rules see what they hold: every file of the project but the harness, the test classes among them.
        files.Select(file => file.Name).ShouldContain("ReferenceHostTests.cs");
        Harness.ShouldAllBe(name => files.Any(file => file.Name == name), "a file of the harness list is gone; take it out of the list");
        var held = files.Where(file => !Harness.Contains(file.Name)).ToList();
        held.Count.ShouldBeGreaterThan(15);
        held.SelectMany(file => Violations(file.Name, file.Source)).ShouldBeEmpty();
    }

    [Fact]
    public void AFileThatBreaksEachRuleIsFound()
    {
        const string careless = """
            using System.Diagnostics;
            using static System.Threading.Tasks.Task;

            public sealed class CarelessTests(EndToEndHost host) : BrowserTest(host)
            {
                public async Task ReadsAndSleeps(IResponse response, IPage page)
                {
                    var body = await response.TextAsync();
                    var json = await response.JsonAsync<Dictionary<string, List<int>>>();
                    Func<Task<byte[]>> bytes = response.BodyAsync;
                    await page.WaitForTimeoutAsync(500);
                    await Task
                        .Delay(100);
                    await Delay(100);
                    Func<Task> later = () => Task.Delay(5);
                    Thread.Sleep(100);
                    await page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve, 50))");
                    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
                    var watch = Stopwatch.StartNew();
                    var ticks = Environment.TickCount64;
                    // A comment may name setTimeout, TextAsync or Task.Delay without breaking a rule.
                    var text = await page.Locator("p").InnerTextAsync();
                }

                private static async ValueTask PressAsync(ILocator target, string key) => await target.PressAsync(key);

                private static Task FocusReturnedAsync(ILocator opener) => Task.CompletedTask;
            }

            internal static class Helpers
            {
                internal static Task FocusAsync(this ILocator target) => target.FocusAsync();
            }
            """;

        var found = Violations("CarelessTests.cs", careless).ToList();

        found.Count(violation => violation.Contains(Body, StringComparison.Ordinal)).ShouldBe(3);
        found.Count(violation => violation.Contains(Timer, StringComparison.Ordinal)).ShouldBe(6);
        found.Count(violation => violation.Contains(Clock, StringComparison.Ordinal)).ShouldBe(3);
        found.Count(violation => violation.Contains(Copy, StringComparison.Ordinal)).ShouldBe(3);
        found.Count.ShouldBe(15);
    }

    // Each name that breaks a rule (but in a using directive, which names a namespace or a type to import), each string that
    // sets a timer in the page, and each declaration of a helper of Ui's.
    private static IEnumerable<string> Violations(string file, string source)
    {
        var syntax = CSharpSyntaxTree.ParseText(source.ReplaceLineEndings("\n")).GetRoot();

        string At(SyntaxNode node, string rule) =>
            $"{Folder}/{file}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1} {rule}: {node.ToString().ReplaceLineEndings(" ")}";

        foreach (var name in syntax.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (Names.TryGetValue(name.Identifier.ValueText, out var rule) && !name.Ancestors().OfType<UsingDirectiveSyntax>().Any())
            {
                yield return At(name, rule);
            }
        }

        foreach (var text in syntax.DescendantTokens().Where(IsText))
        {
            if (text.ValueText.Contains("setTimeout", StringComparison.Ordinal) || text.ValueText.Contains("setInterval", StringComparison.Ordinal))
            {
                yield return At(text.Parent!, Timer);
            }
        }

        foreach (var declaration in syntax.DescendantNodes())
        {
            var declared = declaration switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                LocalFunctionStatementSyntax local => local.Identifier.ValueText,
                _ => null,
            };
            if (declared is not null && Copies.Contains(declared))
            {
                yield return At(declaration, Copy);
            }
        }
    }

    private static bool IsText(SyntaxToken token) =>
        token.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken;
}
