using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Hooks;
using FluentCMS.Plugins;
using FluentCMS.Settings;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

internal sealed record ProblemError(string Path, string Code, string Message);

internal sealed record ApiProblem(
    int Status,
    string Code,
    string Title,
    string Detail,
    IReadOnlyList<ProblemError>? Errors = null,
    ContentDocument? Current = null);

// The one place that decides what a client sees for an exception of the engine.
internal static class ProblemMapper
{
    public const string TypePrefix = "urn:fluentcms:error:";

    // A host's codes, with those of its plugins; without them, a plugin's code is answered as one it did not add.
    public static ApiProblem Map(Exception exception, bool authenticated, ErrorCodeCatalogue? codes = null) => exception switch
    {
        ContentAccessDeniedException denied => Denied(denied.Message, authenticated),
        SettingsAccessDeniedException denied => Denied(denied.Message, authenticated),
        ServiceUnavailableException unavailable => Known(ApiErrorCodes.Unavailable, unavailable.Message),
        InstanceAccessDeniedException denied => Denied(denied.Message, authenticated),
        ManagementAccessDeniedException denied => Denied(denied.Message, authenticated),
        PermissionDeniedException denied => Denied(denied.Message, authenticated),
        ScopeDeniedException denied => Denied(denied.Message, authenticated),
        MemberAccessDeniedException denied => Denied(denied.Message, authenticated),
        MemberRegistrationClosedException closed => Known(ApiErrorCodes.NotFound, closed.Message),
        SignInRequiredException required => Known(ApiErrorCodes.Unauthorized, required.Message),
        RequestAuthenticationException unauthenticated => Known(unauthenticated.Code ?? ApiErrorCodes.Unauthorized, unauthenticated.Message),
        EntryNotFoundException notFound => Known(ApiErrorCodes.NotFound, notFound.Message),
        PluginDisabledException disabled => Known(PluginErrorCodes.PluginDisabled, disabled.Message),
        VersionConflictException conflict => Known(ApiErrorCodes.VersionConflict, conflict.Message) with { Current = conflict.Current },
        ValidationException validation => FromValidation(validation, codes ?? ErrorCodeCatalogue.Core),
        HookRejectedException rejected => Refused(rejected.Code, rejected.Message, "Rejected by a hook", codes ?? ErrorCodeCatalogue.Core),
        PluginException refused => Refused(refused.Code, refused.Message, "Refused by a plugin", codes ?? ErrorCodeCatalogue.Core),
        RichTextValidationException richText => FromRichText(richText),
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } tooLarge => Known(LimitErrorCodes.RequestTooLarge, tooLarge.Message),
        BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } mediaType => Known(ApiErrorCodes.UnsupportedMediaType, mediaType.Message),
        BadHttpRequestException badRequest => Known(ApiErrorCodes.BadRequest, badRequest.Message) with { Status = badRequest.StatusCode },
        _ => Known(ApiErrorCodes.InternalError, "An unexpected error occurred."),
    };

    // The problem of one entry of a bulk result, which carries the code the single call's problem would have; a code the
    // catalogue does not know is a hook's, as in Map.
    public static ApiProblem Map(BulkProblem problem, bool authenticated, ErrorCodeCatalogue? codes = null)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var catalogue = codes ?? ErrorCodeCatalogue.Core;
        if (problem.Errors.Count > 0)
        {
            return FromErrors(problem.Errors, catalogue);
        }

        return problem.Code switch
        {
            ApiErrorCodes.Forbidden => Denied(problem.Message, authenticated),
            ApiErrorCodes.VersionConflict => Known(ApiErrorCodes.VersionConflict, problem.Message) with { Current = problem.Current },
            _ => Refused(problem.Code, problem.Message, "Rejected by a hook", catalogue),
        };
    }

    public static ApiProblem Known(string code, string detail)
    {
        var info = ErrorCodeRegistry.Find(code)!;
        return new ApiProblem(info.Status, info.Code, info.Title, detail);
    }

    // The engine does not say whether the principal is signed in, so the request does: a client that is not should sign in.
    private static ApiProblem Denied(string detail, bool authenticated) =>
        Known(authenticated ? ApiErrorCodes.Forbidden : ApiErrorCodes.Unauthorized, detail);

    private static ApiProblem FromValidation(ValidationException exception, ErrorCodeCatalogue catalogue) =>
        FromErrors(exception.Errors, catalogue);

    private static ApiProblem FromErrors(IReadOnlyList<ValidationError> validationErrors, ErrorCodeCatalogue catalogue)
    {
        var errors = validationErrors.Select(error => new ProblemError(error.Path, error.Code, error.Message)).ToList();
        var codes = errors.Select(error => error.Code).Distinct().ToList();
        var code = codes.Count == 1 ? codes[0] : ApiErrorCodes.ValidationFailed;

        var statuses = codes.Select(item => catalogue.Find(item)?.Status ?? StatusCodes.Status422UnprocessableEntity).Distinct().ToList();
        var status = statuses.Count == 1 ? statuses[0] : StatusCodes.Status422UnprocessableEntity;

        var title = catalogue.Find(code)?.Title ?? ErrorCodeRegistry.Find(ApiErrorCodes.ValidationFailed)!.Title;
        var detail = errors.Count == 1 ? errors[0].Message : $"The request has {errors.Count} errors; see errors.";
        return new ApiProblem(status, code, title, detail, errors);
    }

    private static ApiProblem Refused(string code, string message, string title, ErrorCodeCatalogue catalogue)
    {
        var info = catalogue.Find(code);
        return new ApiProblem(info?.Status ?? StatusCodes.Status422UnprocessableEntity, code, info?.Title ?? title, message);
    }

    private static ApiProblem FromRichText(RichTextValidationException exception)
    {
        var errors = exception.Errors
            .Select(error => new ProblemError(error.Path, ValidationErrorCodes.InvalidValue, error.Message))
            .ToList();
        var info = ErrorCodeRegistry.Find(ApiErrorCodes.ValidationFailed)!;
        return new ApiProblem(info.Status, info.Code, info.Title, errors.Count == 1 ? errors[0].Message : "The rich text document is not valid; see errors.", errors);
    }
}
