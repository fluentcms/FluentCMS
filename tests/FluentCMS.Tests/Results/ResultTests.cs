using FluentCMS.Core.Results;

namespace FluentCMS.Tests.Results;

public sealed class ResultTests
{
    [Fact]
    public void A_successful_result_exposes_its_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void A_failed_result_keeps_its_errors_and_refuses_to_give_a_value()
    {
        var result = Result<int>.Failure("too_small", "The value is too small.");

        Assert.False(result.IsSuccess);
        Assert.Equal("too_small", Assert.Single(result.Errors).Code);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("too small", exception.Message);
    }
}
