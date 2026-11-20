using Shouldly;
using Xunit;

namespace FluentCMS.Architecture.Tests;

// A copy of the compose example is what an operator starts from, so it lifts no refusal of the host; the opt-ins of an
// evaluation are there to uncomment, and compose.yaml defaults both to false.
public class ComposeExampleTests
{
    private static readonly string[] OptIns = ["FLUENTCMS_ALLOW_GENERATED_SIGNING_KEY", "FLUENTCMS_ALLOW_UNENCRYPTED_KEY_RING"];

    [Fact]
    public void TheExampleNamesTheEvaluationOptInsAndTurnsNoneOn()
    {
        var lines = File.ReadAllLines(Path.Combine(ProjectFile.FindRepositoryRoot(), "src", "FluentCMS.Host", ".env.example"))
            .Select(line => line.Trim())
            .ToList();
        var set = lines.Where(line => !line.StartsWith('#')).Select(line => line.Split('=', 2)[0]).ToList();

        foreach (var optIn in OptIns)
        {
            set.ShouldNotContain(optIn);
            lines.ShouldContain($"# {optIn}=true");
        }

        set.ShouldNotContain(name => name.StartsWith("FLUENTCMS_ALLOW_", StringComparison.Ordinal));
    }
}
