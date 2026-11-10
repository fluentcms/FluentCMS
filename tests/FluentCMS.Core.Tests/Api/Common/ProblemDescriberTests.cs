using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content;
using FluentCMS.Plugins;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

// What a plugin surface such as MCP is told an exception is: the problem the REST APIs answer it with.
public sealed class ProblemDescriberTests
{
    private static readonly SiteId Site = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static readonly ErrorCodeCatalogue Codes = new(new Dictionary<string, ErrorCodeInfo> { ["ACME0001"] = new("ACME0001", 409, "Note exists") });

    [Fact]
    public void AValidationFailureKeepsItsCodeAndItsErrors()
    {
        var describer = Describer(SignedIn());

        var problem = describer.Describe(new ValidationException(
        [
            new ValidationError("title", ValidationErrorCodes.Required, "A value is required."),
            new ValidationError("slug", ValidationErrorCodes.Required, "A value is required."),
        ]));

        (problem.Code, problem.Status).ShouldBe((ValidationErrorCodes.Required, 422));
        problem.Errors.Select(error => (error.Path, error.Code, error.Message)).ShouldBe(
        [
            ("title", ValidationErrorCodes.Required, "A value is required."),
            ("slug", ValidationErrorCodes.Required, "A value is required."),
        ]);
    }

    // The operation says whether the caller is signed in, which the HTTP layer reads from the request.
    [Fact]
    public void ARefusedPermissionIsForbiddenForASignedInCallerAndUnauthorizedOtherwise()
    {
        var denied = new PermissionDeniedException("media.manage", Site);

        var signedIn = Describer(SignedIn()).Describe(denied);
        var anonymous = Describer(new OperationContext(Principal.Anonymous, Origin.Mcp, Site, "en", null, "test", DateTimeOffset.UnixEpoch)).Describe(denied);
        var outside = Describer(null).Describe(denied);

        (signedIn.Code, signedIn.Status, signedIn.Detail).ShouldBe(("FCMS3103", 403, denied.Message));
        (anonymous.Code, anonymous.Status).ShouldBe(("FCMS3102", 401));
        (outside.Code, outside.Status).ShouldBe(("FCMS3102", 401));
    }

    [Fact]
    public void ACodeOfAPluginHasTheStatusAndTitleThePluginGaveIt()
    {
        var problem = Describer(SignedIn()).Describe(new PluginException("ACME0001", "A note 'first' exists."));

        (problem.Code, problem.Status, problem.Title, problem.Detail).ShouldBe(("ACME0001", 409, "Note exists", "A note 'first' exists."));
        problem.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void AnExceptionThatSaysNothingOfItsCauseKeepsItsMessageToItself()
    {
        var problem = Describer(SignedIn()).Describe(new InvalidOperationException("Server=db;Password=hunter2"));

        (problem.Code, problem.Status).ShouldBe(("FCMS3199", 500));
        problem.Detail.ShouldNotContain("hunter2");
    }

    private static ProblemDescriber Describer(OperationContext? operation) => new(new OperationContextAccessor { Current = operation }, Codes);

    private static OperationContext SignedIn() =>
        new(new Principal(PrincipalKind.AdminUser, Guid.NewGuid(), "Ada"), Origin.Mcp, Site, "en", "client", "test", DateTimeOffset.UnixEpoch);
}
