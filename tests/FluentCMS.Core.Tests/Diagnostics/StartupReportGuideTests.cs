using System.Globalization;
using FluentCMS.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Diagnostics;

// An operator filters the startup report in the logs by the event ids the host guide names.
public sealed class StartupReportGuideTests
{
    [Fact]
    public void TheHostGuideNamesTheEventIdsOfTheReport()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "host-configuration.md"));

        guide.ShouldContain($"(event id {Id(StartupReportEvents.Report)}, Information)");
        guide.ShouldContain($"(event id {Id(StartupReportEvents.InsecureDefault)})");
        guide.ShouldContain($"(event id {Id(StartupReportEvents.NotMade)},");
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
