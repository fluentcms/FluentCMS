using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Commands;

public sealed class ImportOutputTests
{
    [Fact]
    public async Task AReportThatLeavesItemsOutSaysHowMany()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        const string Line = """{"report":{"dryRun":false,"total":{"created":0,"updated":0,"skipped":0,"conflicts":0,"errors":4},"sections":{"article":{"created":0,"updated":0,"skipped":0,"conflicts":0,"errors":4}},"items":[{"section":"article","id":"line 1","outcome":"error","code":"FCMS3212","message":"Not an entry"}],"omitted":3}}""";

        (await ImportOutput.WriteReportAsync(Line, json: false, output, error)).ShouldBe(1);

        output.ToString().ShouldContain("error article line 1: FCMS3212 Not an entry");
        output.ToString().ShouldContain("3 more conflicts and errors are counted and not listed.");
    }
}
