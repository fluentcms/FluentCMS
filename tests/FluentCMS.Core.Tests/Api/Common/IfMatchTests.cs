using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class IfMatchTests
{
    [Theory]
    [InlineData("\"7\"", 7)]
    [InlineData(" \"0\" ", 0)]
    public void AStrongTagIsTheVersion(string header, int version)
    {
        IfMatch.Required(Request(header)).ShouldBe(version);
        IfMatch.Optional(Request(header)).ShouldBe(version);
    }

    // A proxy that compresses responses weakens the ETag it passes on, and the client sends back what it was given.
    [Theory]
    [InlineData("W/\"7\"", 7)]
    [InlineData(" W/\"0\" ", 0)]
    public void AWeakTagIsReadByItsOpaqueValue(string header, int version)
    {
        IfMatch.Required(Request(header)).ShouldBe(version);
        IfMatch.Optional(Request(header)).ShouldBe(version);
    }

    [Fact]
    public void NoneOrAnyIsNoVersionWhereOneIsOptional()
    {
        IfMatch.Optional(Request(null)).ShouldBeNull();
        IfMatch.Optional(Request("*")).ShouldBeNull();
    }

    [Fact]
    public void ARequiredVersionThatIsMissingIsPreconditionRequired()
    {
        var error = Should.Throw<ValidationException>(() => IfMatch.Required(Request(null))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe("If-Match");
        error.Code.ShouldBe(PreconditionErrorCodes.IfMatchRequired);
        ErrorCodeRegistry.Find(error.Code)!.Status.ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/*")]
    [InlineData("W/7")]
    [InlineData("w/\"7\"")]
    [InlineData("W/\"seven\"")]
    [InlineData("W/\"7\", W/\"8\"")]
    [InlineData("\"7\", \"8\"")]
    [InlineData("7")]
    [InlineData("\"-1\"")]
    [InlineData("\"seven\"")]
    [InlineData("\"\"")]
    [InlineData("\"99999999999\"")]
    public void AnythingElseIsRefused(string header)
    {
        var error = Should.Throw<ValidationException>(() => IfMatch.Required(Request(header))).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(PreconditionErrorCodes.IfMatchInvalid);
        if (header != "*")
        {
            Should.Throw<ValidationException>(() => IfMatch.Optional(Request(header)));
        }
    }

    [Fact]
    public void TwoHeadersAreRefused()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = new(["\"1\"", "\"2\""]);

        Should.Throw<ValidationException>(() => IfMatch.Optional(request)).Errors.ShouldHaveSingleItem().Code.ShouldBe(PreconditionErrorCodes.IfMatchInvalid);
    }

    [Fact]
    public void TheETagIsTheTagOfTheVersion()
    {
        var response = new DefaultHttpContext().Response;

        IfMatch.SetETag(response, 12);

        response.Headers.ETag.ToString().ShouldBe("\"12\"");
    }

    private static HttpRequest Request(string? header)
    {
        var request = new DefaultHttpContext().Request;
        if (header is not null)
        {
            request.Headers.IfMatch = header;
        }

        return request;
    }
}
