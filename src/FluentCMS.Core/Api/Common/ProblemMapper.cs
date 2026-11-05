using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Hooks;
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

    public static ApiProblem Map(Exception exception, bool authenticated) => exception switch
    {
        ContentAccessDeniedException denied => Denied(denied.Message, authenticated),
        SettingsAccessDeniedException denied => Denied(denied.Message, authenticated),
        RequestAuthenticationException unauthenticated => Known(ApiErrorCodes.Unauthorized, unauthenticated.Message),
        EntryNotFoundException notFound => Known(ApiErrorCodes.NotFound, notFound.Message),
        VersionConflictException conflict => Known(ApiErrorCodes.VersionConflict, conflict.Message) with { Current = conflict.Current },
        ValidationException validation => FromValidation(validation),
        HookRejectedException rejected => FromHook(rejected),
        RichTextValidationException richText => FromRichText(richText),
        BadHttpRequestException badRequest => Known(ApiErrorCodes.BadRequest, badRequest.Message) with { Status = badRequest.StatusCode },
        _ => Known(ApiErrorCodes.InternalError, "An unexpected error occurred."),
    };

    private static ApiProblem Known(string code, string detail)
    {
        var info = ErrorCodeRegistry.Find(code)!;
        return new ApiProblem(info.Status, info.Code, info.Title, detail);
    }

    // The engine does not say whether the principal is signed in, so the request does: a client that is not should sign in.
    private static ApiProblem Denied(string detail, bool authenticated) =>
        Known(authenticated ? ApiErrorCodes.Forbidden : ApiErrorCodes.Unauthorized, detail);

    private static ApiProblem FromValidation(ValidationException exception)
    {
        var errors = exception.Errors.Select(error => new ProblemError(error.Path, error.Code, error.Message)).ToList();
        var codes = errors.Select(error => error.Code).Distinct().ToList();
        var code = codes.Count == 1 ? codes[0] : ApiErrorCodes.ValidationFailed;

        var statuses = codes.Select(item => ErrorCodeRegistry.Find(item)?.Status ?? StatusCodes.Status422UnprocessableEntity).Distinct().ToList();
        var status = statuses.Count == 1 ? statuses[0] : StatusCodes.Status422UnprocessableEntity;

        var title = ErrorCodeRegistry.Find(code)?.Title ?? ErrorCodeRegistry.Find(ApiErrorCodes.ValidationFailed)!.Title;
        var detail = errors.Count == 1 ? errors[0].Message : $"The request has {errors.Count} errors; see errors.";
        return new ApiProblem(status, code, title, detail, errors);
    }

    private static ApiProblem FromHook(HookRejectedException exception)
    {
        var info = ErrorCodeRegistry.Find(exception.Code);
        return new ApiProblem(
            info?.Status ?? StatusCodes.Status422UnprocessableEntity,
            exception.Code,
            info?.Title ?? "Rejected by a hook",
            exception.Message);
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
