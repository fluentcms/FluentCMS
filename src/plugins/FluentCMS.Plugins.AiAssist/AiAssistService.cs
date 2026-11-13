using System.Text;
using System.Text.Json;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Localization;
using FluentCMS.Media;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.AiAssist;

// The body of POST /ai/actions/{action}: the field the suggestion is for, and the text to work on or the entry whose
// draft holds it.
public sealed record AiActionRequest(string? Type, string? Field, string? Locale, string? Text, Guid? DocumentId, AiActionOptions? Options);

// TargetLocale is translate's, an enabled locale of the site, and MediaId altText's, an image of the media library.
public sealed record AiActionOptions(string? TargetLocale, Guid? MediaId);

// Suggestion is the text, and for seo an AiSeoSuggestion.
public sealed record AiActionResult(object Suggestion, AiUsageInfo Usage);

public sealed record AiSeoSuggestion(string MetaTitle, string MetaDescription);

public sealed record AiUsageInfo(string Provider, string Model, long PromptTokens, long CompletionTokens, long DurationMs);

// A call that passed every check and is ready for its provider.
internal sealed record AiCall(
    string Action,
    SiteId Site,
    Principal Principal,
    string Type,
    string Field,
    AiClient Client,
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions Options,
    TimeSpan Timeout);

// The actions of spec 5.5, in the site of the current operation. PrepareAsync checks, in this order: ai.use in the site,
// that the site's settings opt the field in, and that the caller may update that field, since a suggestion is for a field
// the caller edits. It then reads what the action works on as the caller, through the content and media services, asks
// the slot for the site's client and counts the call. RunAsync sends it and writes its usage row: one for every call
// that is sent, whether it succeeds, fails, times out or is cancelled. The plugin writes nothing to content, and the text
// and the suggestion go to the provider and the caller only: never to a row, a log line or a span.
internal sealed class AiAssistService(
    IOperationContextAccessor operations,
    IPermissionChecker permissions,
    IEffectiveAccess access,
    ISettings<AiSettings> settings,
    SchemaModel schema,
    IContentService content,
    ILocaleResolver locales,
    IPluginState plugins,
    IServiceProvider services,
    IAiClientFactory clients,
    AiCallLimits limits,
    AiUsageService usage,
    IOptions<AiOptions> options,
    TimeProvider time,
    ILogger<AiAssistService> logger)
{
    public const string MediaPlugin = "media";

    // The longer side of the copy of an image that altText sends.
    public const int MaxImageSide = 1024;

    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string TimedOut = "timeout";
    public const string Cancelled = "cancelled";

    private static readonly string[] TextFieldTypes = ["text", "longText", RichTextFieldType];

    private const string RichTextFieldType = "richText";

    public async Task<AiCall> PrepareAsync(string action, AiActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = operations.Current ?? throw new InvalidOperationException("An AI action runs inside an operation, which names the site and the caller.");
        var site = operation.Site;
        await permissions.RequireAsync(AiPermissions.Use, site, cancellationToken);
        if (!AiActions.IsKnown(action))
        {
            throw new PluginException(AiErrorCodes.UnknownAction, $"There is no such AI action. The actions are {string.Join(", ", AiActions.Names)}.");
        }

        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Field))
        {
            throw new ValidationException([Invalid(string.IsNullOrWhiteSpace(request.Type) ? "type" : "field", "An action names the content type and the field its suggestion is for.")]);
        }

        var current = await settings.GetAsync(site, cancellationToken);
        var type = schema.FindContentType(request.Type.Trim());
        var field = type?.FindField(request.Field.Trim());
        if (type is null || field is null || !TakesActions(field)
            || !current.Fields.TryGetValue(type.Name, out var optedIn) || !optedIn.Contains(field.Name, StringComparer.Ordinal))
        {
            throw new PluginException(AiErrorCodes.FieldNotOptedIn, "The AI settings of the site do not opt this field in. A field that takes AI actions is a text, long text or rich text field of a content type, named under 'fields'.");
        }

        // The locale comes before the right to update, because a grant may be limited to locales: the right is asked in
        // the locale the suggestion is for, which is also the one the draft is read in.
        List<ValidationError> errors = [];
        var locale = await LocaleAsync(site, request.Locale, operation.Locale, "locale", errors, cancellationToken);
        if (locale is null)
        {
            throw new ValidationException(errors);
        }

        var held = await AccessInAsync(operation, type.Localized ? locale : operation.Locale, cancellationToken);
        if (!held.Types.TryGetValue(type.Name, out var typeAccess) || !typeAccess.Update || !typeAccess.UpdatableFields.Contains(field.Name))
        {
            throw new ContentAccessDeniedException(type.Name, "update");
        }

        string? targetLocale = null;
        if (action == AiActions.Translate)
        {
            targetLocale = await LocaleAsync(site, request.Options?.TargetLocale, null, "options.targetLocale", errors, cancellationToken);
        }
        else if (request.Options?.TargetLocale is not null)
        {
            errors.Add(Invalid("options.targetLocale", "Only translate takes a target locale."));
        }

        if (action == AiActions.AltText ? request.Options?.MediaId is null : request.Options?.MediaId is not null)
        {
            errors.Add(Invalid("options.mediaId", "altText, and no other action, takes the id of an image of the media library."));
        }

        if (request.Text is not null && request.DocumentId is not null)
        {
            errors.Add(Invalid("documentId", "An action takes the text or the id of the entry that holds it, not both."));
        }
        else if (action != AiActions.AltText && request.DocumentId is null && string.IsNullOrWhiteSpace(request.Text))
        {
            errors.Add(Invalid("text", "An action needs a text, or the id of the entry whose draft holds it."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var richText = field.FieldType == RichTextFieldType;
        var text = request.DocumentId is { } id ? await ReadFieldAsync(type, field, id, locale, richText, cancellationToken) : request.Text;
        if (action != AiActions.AltText && string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException([Invalid("documentId", "The field of the draft holds no text.")]);
        }

        if (text?.Length > options.Value.MaxInputLength)
        {
            throw new PluginException(AiErrorCodes.InputTooLong, $"The text has more than the {options.Value.MaxInputLength} characters an AI action takes.");
        }

        List<AIContent> message = [];
        if (action == AiActions.AltText)
        {
            message.Add(await ReadImageAsync(site, request.Options!.MediaId!.Value, cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            message.Add(new TextContent(text));
        }

        // Whichever factory fills the slot: a host's own may not look at the provider.
        if (AiProviders.IsRemoved(current.Provider))
        {
            throw AiProviders.Refuse(logger, site, current.Provider);
        }

        var client = await clients.CreateAsync(site, current, cancellationToken)
            ?? throw new PluginException(AiErrorCodes.NoProvider, "The site has no AI provider. An administrator names one in the AI settings of the site.");

        if (limits.Take(site, operation.Principal, current.CallsPerMinute) is { } refusal)
        {
            if (refusal.Report)
            {
                AiLog.CallLimitExceeded(logger, operation.Principal.Kind, operation.Principal.Id, refusal.Limit, site.Value, refusal.RetryAfterSeconds);
            }

            throw new PluginException(AiErrorCodes.CallLimitExceeded, $"The site allows {refusal.Limit} AI calls a minute for each caller. Try again in {refusal.RetryAfterSeconds} seconds.");
        }

        return new AiCall(
            action,
            site,
            operation.Principal,
            type.Name,
            field.Name,
            client,
            [
                new ChatMessage(ChatRole.System, AiActions.Instructions(action, type.Name, field.Name, richText, field.MaxLength, locale, targetLocale)),
                new ChatMessage(ChatRole.User, message),
            ],
            new ChatOptions { MaxOutputTokens = current.MaxOutputTokens },
            TimeSpan.FromSeconds(current.RequestTimeout));
    }

    // With onDelta the provider streams, and each piece of text goes to it as it arrives; seo, whose suggestion is an
    // object that exists only at the end, streams none. The call ends at its timeout or when the caller goes away.
    public async Task<AiActionResult> RunAsync(AiCall call, Func<string, CancellationToken, ValueTask>? onDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        var started = time.GetTimestamp();
        using var span = AiTelemetry.Start(call.Action, call.Site, call.Type, call.Client.Provider);
        using var timeout = new CancellationTokenSource(call.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var outcome = Succeeded;
        string? code = null;
        Exception? refused = null;
        object? suggestion = null;
        var used = new UsageDetails();
        try
        {
            string text;
            if (onDelta is null)
            {
                var response = await call.Client.Client.GetResponseAsync(call.Messages, call.Options, linked.Token);
                if (response.Usage is not null)
                {
                    used.Add(response.Usage);
                }

                text = response.Text;
            }
            else
            {
                // A provider may send its usage in parts, the prompt first and the completion last, so they add up.
                var answer = new StringBuilder();
                await foreach (var update in call.Client.Client.GetStreamingResponseAsync(call.Messages, call.Options, linked.Token))
                {
                    foreach (var part in update.Contents.OfType<UsageContent>())
                    {
                        used.Add(part.Details);
                    }

                    if (update.Text is { Length: > 0 } delta)
                    {
                        answer.Append(delta);
                        if (call.Action != AiActions.Seo)
                        {
                            await SendAsync(onDelta, delta, linked.Token);
                        }
                    }
                }

                text = answer.ToString();
            }

            suggestion = Suggestion(call.Action, text);
            if (suggestion is null)
            {
                (outcome, code) = (Failed, AiErrorCodes.AnswerUnusable);
                refused = new PluginException(code, call.Action == AiActions.Seo
                    ? "The AI provider did not answer with a meta title and a meta description."
                    : "The AI provider answered with no text.");
            }
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested || exception is CallerGoneException)
            {
                (outcome, refused) = (Cancelled, new OperationCanceledException("The caller went away before the AI call ended.", cancellationToken));
            }
            else if (timeout.IsCancellationRequested)
            {
                (outcome, code) = (TimedOut, AiErrorCodes.TimedOut);
                refused = new PluginException(code, $"The AI provider did not answer within the {call.Timeout.TotalSeconds:0} seconds the site allows.");
            }
            else
            {
                // The type and the status only: the message of a provider may repeat what it was sent.
                (outcome, code) = (Failed, AiErrorCodes.ProviderFailed);
                AiLog.ProviderFailed(logger, call.Client.Provider, call.Action, call.Site.Value, exception.GetType().Name, AiProviderFailures.StatusOf(exception));
                refused = new PluginException(code, "The AI provider failed the call.");
            }
        }

        var info = new AiUsageInfo(
            call.Client.Provider,
            call.Client.Model,
            used.InputTokenCount ?? 0,
            used.OutputTokenCount ?? 0,
            (long)time.GetElapsedTime(started).TotalMilliseconds);
        await RecordAsync(call, info, outcome, code);
        AiTelemetry.End(span, call.Action, call.Client.Provider, outcome, code);
        return refused is null ? new AiActionResult(suggestion!, info) : throw refused;
    }

    private static bool TakesActions(SchemaField field) =>
        TextFieldTypes.Contains(field.FieldType, StringComparer.Ordinal) && !field.Multiple && !field.Hidden && !field.ReadOnly;

    private static ValidationError Invalid(string path, string message) => new(path, AiErrorCodes.InvalidRequest, message);

    // What the caller may do with content in a locale. IEffectiveAccess answers for the operation, so the question is
    // asked inside the caller's own operation with that locale, which is put back at once.
    private async ValueTask<ContentAccess> AccessInAsync(OperationContext operation, string locale, CancellationToken cancellationToken)
    {
        if (locale == operation.Locale)
        {
            return await access.ContentAsync(cancellationToken);
        }

        operations.Current = operation with { Locale = locale };
        try
        {
            return await access.ContentAsync(cancellationToken);
        }
        finally
        {
            operations.Current = operation;
        }
    }

    // A locale the body names is one the site lists, as the locale resolver reads it. Null when it was refused.
    private async Task<string?> LocaleAsync(SiteId site, string? named, string? fallback, string path, List<ValidationError> errors, CancellationToken cancellationToken)
    {
        var locale = fallback;
        if (!string.IsNullOrWhiteSpace(named))
        {
            try
            {
                locale = await locales.ResolveAsync(new LocaleRequest(site) { Parameter = named }, cancellationToken);
            }
            catch (ValidationException)
            {
                locale = null;
            }
        }

        if (locale is null || locale == ILocaleResolver.All)
        {
            errors.Add(Invalid(path, $"'{path}' names one locale that the site has."));
            return null;
        }

        return locale;
    }

    // The draft, as the caller: the content service refuses a caller who may not read the type, the entry or the field.
    private async Task<string?> ReadFieldAsync(SchemaType type, SchemaField field, Guid id, string locale, bool richText, CancellationToken cancellationToken)
    {
        var read = new GetOptions
        {
            Status = QueryStatus.Draft,
            Locale = type.Localized ? locale : null,
            Select = [field.Name],
            RichTextFormat = richText ? RichTextFormats.Markdown : null,
        };
        var entry = await content.GetAsync(type.Name, id, read, cancellationToken) ?? throw new EntryNotFoundException(type.Name, id);
        return entry.TryGetValue(field.Name, out var value) ? value as string : null;
    }

    // A reduced copy without metadata, as the caller: the media service refuses a caller without media.read there.
    private async Task<DataContent> ReadImageAsync(SiteId site, Guid id, CancellationToken cancellationToken)
    {
        var media = services.GetService<IMediaService>();
        if (media is null || !await plugins.IsEnabledAsync(MediaPlugin, site, cancellationToken))
        {
            throw new PluginException(AiErrorCodes.ImageNotAvailable, "The site has no media library, so there is no image to describe.");
        }

        var asset = await media.GetAsync(id, cancellationToken);
        if (!asset.MimeType.StartsWith("image/", StringComparison.Ordinal))
        {
            throw new PluginException(AiErrorCodes.ImageNotAvailable, "The media asset is not an image.");
        }

        var reduce = asset.Width is not <= MaxImageSide || asset.Height is not <= MaxImageSide;
        var copy = await media.OpenImageAsync(
            id,
            new MediaImageOptions
            {
                Width = reduce ? MaxImageSide : null,
                Height = reduce ? MaxImageSide : null,
                Format = asset.MimeType == "image/jpeg" ? MediaImageFormat.Jpeg : MediaImageFormat.Png,
            },
            cancellationToken);
        await using (copy.Content)
        {
            using var bytes = new MemoryStream();
            await copy.Content.CopyToAsync(bytes, cancellationToken);
            return new DataContent(bytes.ToArray(), copy.MimeType);
        }
    }

    // A piece that cannot be written means the caller went away, whatever the write throws, a cancellation of its own
    // included; only the end of the call itself, its timeout or the request's cancellation, is let through as it is.
    private static async ValueTask SendAsync(Func<string, CancellationToken, ValueTask> onDelta, string delta, CancellationToken cancellationToken)
    {
        try
        {
            await onDelta(delta, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CallerGoneException(exception);
        }
    }

    // The text, or for seo its object. Null when the answer is not what the action asks for.
    private static object? Suggestion(string action, string? answer)
    {
        var text = answer?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return action == AiActions.Seo ? Seo(text) : text;
    }

    // The object, with or without a code fence or words around it.
    private static AiSeoSuggestion? Seo(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var document = JsonDocument.Parse(text[start..(end + 1)]);
                if (Member(document.RootElement, "metaTitle") is { } title && Member(document.RootElement, "metaDescription") is { } description)
                {
                    return new AiSeoSuggestion(title, description);
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static string? Member(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim() is { Length: > 0 } text ? text : null;

    // A row that cannot be written must not take the suggestion from a caller whose call was paid for.
    private async Task RecordAsync(AiCall call, AiUsageInfo info, string outcome, string? code)
    {
        try
        {
            await usage.RecordAsync(new AiUsage
            {
                Id = Guid.CreateVersion7(),
                SiteId = call.Site.Value,
                PrincipalKind = AiUsageService.KindOf(call.Principal.Kind),
                PrincipalId = call.Principal.Id,
                Action = call.Action,
                Type = call.Type,
                Field = call.Field,
                Provider = Limit(info.Provider, AiUsageModel.ProviderMaxLength),
                Model = Limit(info.Model, AiUsageModel.ModelMaxLength),
                PromptTokens = info.PromptTokens,
                CompletionTokens = info.CompletionTokens,
                DurationMs = info.DurationMs,
                Succeeded = outcome == Succeeded,
                CreatedAt = time.GetUtcNow(),
            });
        }
        catch (Exception exception)
        {
            AiLog.UsageNotRecorded(logger, exception, call.Action, call.Principal.Kind, call.Principal.Id, call.Site.Value);
        }

        AiLog.Called(logger, call.Action, call.Type, call.Field, call.Principal.Kind, call.Principal.Id, call.Site.Value, info.Provider, info.Model, outcome, code, info.DurationMs, info.PromptTokens, info.CompletionTokens);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    // The caller's connection failed while a piece of the answer was written to it.
    private sealed class CallerGoneException(Exception inner) : Exception("The caller went away.", inner);
}
