using FluentCMS.Core.Audit;

namespace FluentCMS.Tests.Audit;

public sealed class AuditQueryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_ended_and_empty_windows_and_a_complete_subject_are_valid()
    {
        AuditQuery[] queries =
        [
            new(),
            new() { From = Start },
            new() { To = End },
            new() { From = Start, To = End },
            new() { From = Start, To = Start },
            new() { Subject = new AuditSubject("entry", Guid.NewGuid()) },
        ];

        Assert.All(queries, query => Assert.True(query.Validate().IsSuccess));
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_is_a_failure()
    {
        var result = new AuditQuery { From = End, To = Start }.Validate();

        Assert.Equal("audit_window_inverted", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_subject_without_a_type_is_a_failure(string type)
    {
        var result = new AuditQuery { Subject = new AuditSubject(type, Guid.NewGuid()) }.Validate();

        Assert.Equal("audit_subject_type_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_subject_without_an_id_is_a_failure_and_every_problem_is_reported()
    {
        var result = new AuditQuery { From = End, To = Start, Subject = new AuditSubject(null!, Guid.Empty) }.Validate();

        Assert.Equal(
            ["audit_window_inverted", "audit_subject_type_missing", "audit_subject_id_missing"],
            result.Errors.Select(error => error.Code));
    }
}
