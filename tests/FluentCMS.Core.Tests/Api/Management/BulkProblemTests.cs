using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Content;
using FluentCMS.Hooks;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Management;

// A failed entry of a bulk result carries a problem record, not the exception; the HTTP layer writes it as it writes the
// problem of the single call that threw.
public sealed class BulkProblemTests
{
    public static TheoryData<string> Failures => [.. Exceptions.Keys];

    private static readonly Dictionary<string, Func<Exception>> Exceptions = new()
    {
        ["not-found"] = () => new EntryNotFoundException("article", Guid.Parse("11111111-1111-1111-1111-111111111111")),
        ["denied"] = () => new ContentAccessDeniedException("article", "update"),
        ["conflict"] = () => new VersionConflictException(2, 3, new ContentDocument { ["title"] = "Stored", ["views"] = 7 }),
        ["conflict-hidden"] = () => new VersionConflictException(2, 3, null),
        ["required"] = () => new ValidationException([new ValidationError("title", ValidationErrorCodes.Required, "A value is required.")]),
        ["several"] = () => new ValidationException(
        [
            new ValidationError("title", ValidationErrorCodes.Required, "A value is required."),
            new ValidationError("seo.metaTitle", ValidationErrorCodes.MaxLength, "Must have at most 60 characters, has 80."),
        ]),
        ["singleton"] = () => new ValidationException([new ValidationError("", "FCMS2401", "Home is a singleton and already has its entry.")]),
        ["hook-plugin"] = () => new HookRejectedException("ACME0001", "Titles may not contain the word free."),
        ["hook-engine"] = () => new HookRejectedException("FCMS2405", "A hook changed the draft being published."),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void AFailedEntryIsWrittenAsTheProblemOfTheSingleCall(string failure)
    {
        var exception = Exceptions[failure]();

        foreach (var authenticated in new[] { true, false })
        {
            var item = new BulkItemResult(0, null, BulkItemStatus.Failed) { Problem = ContentService.ProblemOf(exception) };
            var written = ManagementJson.Bulk(new BulkResult([item], false), authenticated)["items"]![0]!["problem"]!.ToJsonString();

            written.ShouldBe(JsonSerializer.Serialize(ProblemWriter.Details(ProblemMapper.Map(exception, authenticated)), JsonSerializerOptions.Web));
        }
    }

    [Fact]
    public void TheRecordHoldsNoExceptionAndSurvivesARoundTrip()
    {
        var problem = ContentService.ProblemOf(new ValidationException([new ValidationError("title", ValidationErrorCodes.Required, "A value is required.")])).ShouldNotBeNull();

        var copy = JsonSerializer.Deserialize<BulkProblem>(JsonSerializer.Serialize(problem, JsonSerializerOptions.Web), JsonSerializerOptions.Web).ShouldNotBeNull();

        (copy.Code, copy.Message).ShouldBe((ValidationErrorCodes.Required, "A value is required."));
        copy.Errors.ShouldBe(problem.Errors);
    }

    [Fact]
    public void AnExceptionThatIsNotARefusalOfTheSingleCallIsNotReported() =>
        ContentService.ProblemOf(new InvalidOperationException("connection string password=hunter2")).ShouldBeNull();

    [Fact]
    public void AFailureThatSaysNothingOfItsCauseIsAnInternalError()
    {
        var item = new BulkItemResult(0, null, BulkItemStatus.Failed) { Problem = new BulkProblem(ApiErrorCodes.InternalError, ContentService.UnexpectedFailure) };

        var problem = ManagementJson.Bulk(new BulkResult([item], false), authenticated: true)["items"]![0]!["problem"]!;

        problem["status"]!.GetValue<int>().ShouldBe(500);
        problem["code"]!.GetValue<string>().ShouldBe(ApiErrorCodes.InternalError);
        problem["detail"]!.GetValue<string>().ShouldBe(ContentService.UnexpectedFailure);
    }
}
