using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Transfer;
using FluentCMS.Transfer;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Transfer;

public sealed class TransferReportTests
{
    [Fact]
    public void TheReportListsConflictsAndErrorsUpToItsLimitAndCountsEveryItem()
    {
        var report = new TransferReport(dryRun: false, maxItems: 2);

        report.Add("article", "a", TransferOutcome.Created);
        report.Add("article", "b", TransferOutcome.Updated);
        report.Add("article", "c", TransferOutcome.Skipped, "FCMS3203", "skipped");
        report.Add("article", "d", TransferOutcome.Conflict, null, "newer");
        report.Add("article", "e", TransferOutcome.Error, "FCMS3212", "first");
        report.Add("article", "f", TransferOutcome.Error, "FCMS3212", "second");
        report.Stop(TransferImporter.PackageSection, "content/article.jsonl", "FCMS3200", "stopped");

        report.Items.Select(item => item.Id).ShouldBe(["d", "e", "content/article.jsonl"]);
        report.Of("article").ShouldBe(new TransferCounts(1, 1, 1, 1, 2));
        report.Of(TransferImporter.PackageSection).ShouldBe(TransferCounts.None with { Errors = 1 });
        report.Total.Errors.ShouldBe(3);
        report.ToResult().Omitted.ShouldBe(1);
    }

    // A message or an id of an item, which a contributor or a line of the package can make as long as it holds.
    [Fact]
    public void TheTextsOfAnItemAreHeldToTheirLength()
    {
        var report = new TransferReport(dryRun: false, maxItems: 10);

        report.Add("article", new string('i', 10_000), TransferOutcome.Error, "FCMS3212", new string('m', 10_000));
        report.Stop(TransferImporter.PackageSection, "content/article.jsonl", "FCMS3200", new string('s', 10_000));

        report.Items.ShouldAllBe(item => item.Message!.Length == TransferReport.MaxTextLength + 1 && item.Message.EndsWith('…'));
        report.Items[0].Id.Length.ShouldBe(TransferReport.MaxTextLength + 1);
        report.Items[1].Id.ShouldBe("content/article.jsonl");
    }

    [Fact]
    public void TheBoundsFollowThePackageSizeUnlessTheyAreSet()
    {
        var options = new TransferOptions { MaxPackageSize = 1000 };
        (options.UnpackedLimit, options.ReadLimit).ShouldBe((1000, 2000));

        options = new TransferOptions { MaxPackageSize = long.MaxValue, MaxUnpackedSize = 5, MaxReadSize = 7 };
        (options.UnpackedLimit, options.ReadLimit).ShouldBe((5, 7));
        new TransferOptions { MaxPackageSize = long.MaxValue }.ReadLimit.ShouldBe(long.MaxValue);
    }

    [Theory]
    [InlineData(nameof(TransferOptions.MaxUnpackedSize))]
    [InlineData(nameof(TransferOptions.MaxReadSize))]
    [InlineData(nameof(TransferOptions.MaxEntries))]
    [InlineData(nameof(TransferOptions.MaxJsonSize))]
    [InlineData(nameof(TransferOptions.MaxLineLength))]
    [InlineData(nameof(TransferOptions.MaxReportItems))]
    public void ABoundThatIsNotMoreThanZeroStopsTheHost(string name)
    {
        var options = new TransferOptions();
        var property = typeof(TransferOptions).GetProperty(name)!;
        property.SetValue(options, property.PropertyType == typeof(int) ? (object)0 : 0L);

        var refused = Should.Throw<FluentCmsConfigurationException>(options.Validate);

        refused.Code.ShouldBe(HostErrorCodes.InvalidOption);
        refused.Detail.ShouldStartWith(name);
    }
}
