using FluentCMS.Core.Diagnostics;
using FluentCMS.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Diagnostics;

// A default found once requests arrive can come before the report is set, or while it is being made.
public sealed class StartupReportHolderTests
{
    private static readonly InsecureDefault Made = new() { Id = "made", Message = "from the factory" };

    [Fact]
    public void AnItemAddedBeforeTheReportIsSetIsInIt()
    {
        var holder = new StartupReportHolder();

        holder.Add(new InsecureDefault { Id = "late", Message = "first" });
        holder.Set(new StartupReport { InsecureDefaults = [Made] });

        holder.Current!.InsecureDefaults.Select(item => item.Id).ShouldBe(["made", "late"]);
    }

    [Fact]
    public void AnItemAddedAfterTheReportReplacesItsEarlierVersionAndSurvivesTheNextSet()
    {
        var holder = new StartupReportHolder();
        holder.Set(new StartupReport { InsecureDefaults = [Made] });

        holder.Add(new InsecureDefault { Id = "late", Message = "first" });
        holder.Add(new InsecureDefault { Id = "late", Message = "second" });
        holder.Set(new StartupReport { InsecureDefaults = [Made] });

        holder.Current!.InsecureDefaults.Select(item => item.Message).ShouldBe(["from the factory", "second"]);
    }

    [Fact]
    public void ClearingTheReportLeavesNoReport()
    {
        var holder = new StartupReportHolder();
        holder.Add(new InsecureDefault { Id = "late", Message = "first" });

        holder.Set(null);

        holder.Current.ShouldBeNull();
    }
}
