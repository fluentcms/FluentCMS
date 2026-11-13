using FluentCMS.Sites;
using FluentCMS.Transfer;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Transfer;

public class TransferImportContractTests
{
    [Fact]
    public void TheShortReportIsTheFullOneWithoutACodeOrAMessage()
    {
        var import = new RecordingImport();

        ((ITransferImport)import).Report("media", "1", TransferOutcome.Skipped);

        import.Reported.ShouldBe([("media", "1", TransferOutcome.Skipped, null, null)]);
    }

    private sealed class RecordingImport : ITransferImport
    {
        public List<(string Section, string Id, TransferOutcome Outcome, string? Code, string? Message)> Reported { get; } = [];

        public SiteId Site => SiteId.Empty;

        public SiteId PackageSite => SiteId.Empty;

        public bool DryRun => false;

        public bool Overwrite => false;

        public IReadOnlyDictionary<string, string> Options { get; } = new Dictionary<string, string>();

        public IReadOnlyList<string> Paths { get; } = [];

        public Task<bool> ReadAsync(string path, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken) => Task.FromResult(false);

        public void Report(string section, string id, TransferOutcome outcome, string? code, string? message) => Reported.Add((section, id, outcome, code, message));
    }
}
