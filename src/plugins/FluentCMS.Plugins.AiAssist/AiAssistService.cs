using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Activity;
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
// draft holds it. altText with neither a type nor a field is for the asset alone, as the media library edits it.
public sealed record AiActionRequest(string? Type, string? Field, string? Locale, string? Text, Guid? DocumentId, AiActionOptions? Options);

// TargetLocale is translate's, an enabled locale of the site, and MediaId altText's, an image of the media library, which
// for a field is one the draft of the entry DocumentId names refers to.
public sealed record AiActionOptions(string? TargetLocale, Guid? MediaId);

// Suggestion is the text, and for seo an AiSeoSuggestion.
public sealed record AiActionResult(object Suggestion, AiUsageInfo Usage);

public sealed record AiSeoSuggestion(string MetaTitle, string MetaDescription);

public sealed record AiUsageInfo(string Provider, string Model, long PromptTokens, long CompletionTokens, long DurationMs);

// The answer of GET /ai/fields: whether the site names a provider, by content type the opted-in fields that the caller
// may run actions on in Locale, the locale of the request, and whether it may run altText on an asset alone. Nothing of
// the provider's configuration.
public sealed record AiFields(bool Enabled, string Locale, IReadOnlyDictionary<string, IReadOnlyList<string>> Fields, bool Assets);

// A call that passed every check and is ready for its provider. Type and Field are empty for altText on an asset alone.
internal sealed record AiCall(
    string Action,
    SiteId Site,
    Principal Principal,
    string Type,
    string Field,
    AiClient Client,
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions Options,
    TimeSpan Timeout,
    long PromptEstimate,
    AiReservation? Reservation);

// The actions of spec 5.5, in the site of the current operation. PrepareAsync checks, in this order: ai.use in the site,
// that the site's settings opt the field in, for altText that they let images go with assetAltText, and that the caller
// may update that field, since a suggestion is for a field the caller edits. It then checks the body. Every altText then
// asks the media service's own check of an update of the asset, which for an asset alone, with no field, stands for the
// right to update; altText for a field then also needs an image that the entry's draft refers to. It then
// takes the call from the call limit, asks the slot for the site's client and reserves the call's estimate against the
// site's daily budget, so that a caller who is over either is refused before any work is done for it, and reads what the
// action works on as the caller, through the content and media services. A call refused on the way is given back to the
// limit and the budget. RunAsync sends it and writes its usage row: one for every call that is sent, whether it succeeds,
// fails, times out or is cancelled. The plugin writes nothing to content, and the text and the suggestion go to the
// provider and the caller only: never to a row, a log line or a span.
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
    AiBudgets budgets,
    AiUsageService usage,
    IActivityLog activity,
    IOptions<AiOptions> options,
    TimeProvider time,
    ILogger<AiAssistService> logger)
{
    public const string MediaPlugin = "media";

    // The longer side of the copy of an image that altText sends.
    public const int MaxImageSide = 1024;

    // How long the usage row of a call may take to write before it counts as not written.
    public static readonly TimeSpan UsageWriteTimeout = TimeSpan.FromSeconds(5);

    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string TimedOut = "timeout";
    public const string Cancelled = "cancelled";

    private static readonly string[] TextFieldTypes = ["text", "longText", RichTextFieldType];

    private const string RichTextFieldType = "richText";

    // The media plugin's field type, and the type of a rich text node that shows an asset.
    private const string MediaFieldType = "media";

    // Core stores rich text up to 128 levels deep, past the reader's default of 64; its constant is internal to core.
    private static readonly JsonDocumentOptions RichTextJson = new() { MaxDepth = 128 };

    private const string MediaRead = "media.read";
    private const string MediaUpdate = "media.update";

    // The fields that pass the checks of PrepareAsync in the locale of the request, so that an editor offers the actions
    // only where they work. A field the caller may set on a new entry only is left out, since PrepareAsync refuses it too.
    public async Task<AiFields> FieldsAsync(CancellationToken cancellationToken)
    {
        var operation = operations.Current ?? throw new InvalidOperationException("The AI fields are read inside an operation, which names the site and the caller.");
        await permissions.RequireAsync(AiPermissions.Use, operation.Site, cancellationToken);

        var current = await settings.GetAsync(operation.Site, cancellationToken);
        var enabled = current.Provider != AiProvider.None && !AiProviders.IsRemoved(current.Provider);
        Dictionary<string, IReadOnlyList<string>> fields = new(StringComparer.Ordinal);
        if (enabled)
        {
            var held = await access.ContentAsync(cancellationToken);
            foreach (var (name, typeAccess) in held.Types)
            {
                if (schema.FindContentType(name) is not { } type)
                {
                    continue;
                }

                var offered = type.Fields.Where(field => IsOptedIn(current, type, field) && MayUpdate(typeAccess, field)).Select(field => field.Name).ToList();
                if (offered.Count > 0)
                {
                    fields[type.Name] = offered;
                }
            }
        }

        return new AiFields(enabled, operation.Locale, fields, enabled && current.AssetAltText && await MayDescribeAssetsAsync(operation.Site, cancellationToken));
    }

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

        var forAsset = action == AiActions.AltText && string.IsNullOrWhiteSpace(request.Type) && string.IsNullOrWhiteSpace(request.Field);
        if (!forAsset && (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Field)))
        {
            throw new ValidationException([Invalid(string.IsNullOrWhiteSpace(request.Type) ? "type" : "field", "An action names the content type and the field its suggestion is for; only altText may name neither, for an asset alone.")]);
        }

        var current = await settings.GetAsync(site, cancellationToken);
        SchemaType? type = null;
        SchemaField? field = null;
        if (!forAsset)
        {
            type = schema.FindContentType(request.Type!.Trim());
            field = type?.FindField(request.Field!.Trim());
            if (type is null || field is null || !IsOptedIn(current, type, field))
            {
                throw new PluginException(AiErrorCodes.FieldNotOptedIn, "The AI settings of the site do not opt this field in. A field that takes AI actions is a text, long text or rich text field of a content type, named under 'fields'.");
            }
        }

        // altText sends an image of the media library, and only it does, whether for a field or for an asset alone: an
        // opted-in text field does not opt in the images the caller may read.
        if (action == AiActions.AltText && !current.AssetAltText)
        {
            throw new PluginException(AiErrorCodes.FieldNotOptedIn, "The AI settings of the site do not let images of the media library go to the provider for alt text. An administrator turns it on with 'assetAltText' in the AI settings of the site.");
        }

        // The locale comes before the right to update, because a grant may be limited to locales: the right is asked in
        // the locale the suggestion is for, which is also the one the draft is read in.
        List<ValidationError> errors = [];
        var locale = await LocaleAsync(site, request.Locale, operation.Locale, "locale", errors, cancellationToken);
        if (locale is null)
        {
            throw new ValidationException(errors);
        }

        ContentAccess? held = null;
        if (type is not null && field is not null)
        {
            held = await AccessInAsync(operation, type.Localized ? locale : operation.Locale, cancellationToken);
            if (!held.Types.TryGetValue(type.Name, out var typeAccess) || !MayUpdate(typeAccess, field))
            {
                throw new ContentAccessDeniedException(type.Name, "update");
            }
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
        else if (forAsset && request.DocumentId is not null)
        {
            errors.Add(Invalid("documentId", "altText for an asset alone names no field, so there is no draft to read."));
        }
        else if (action == AiActions.AltText && !forAsset && request.DocumentId is null)
        {
            errors.Add(Invalid("documentId", "altText for a field takes the id of the entry whose draft refers to the image."));
        }
        else if (action != AiActions.AltText && request.DocumentId is null && string.IsNullOrWhiteSpace(request.Text))
        {
            errors.Add(Invalid("text", "An action needs a text, or the id of the entry whose draft holds it."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // The image first, by the check of the asset route, whose answer does not depend on the entry; the tie to the entry
        // is then asked only for an image the caller may update, and answers only what the caller may read in the draft.
        if (action == AiActions.AltText)
        {
            await RequireUpdatableAsync(site, request.Options!.MediaId!.Value, cancellationToken);
            if (!forAsset)
            {
                await RequireReferencedAsync(held!, type!, request.DocumentId!.Value, locale, request.Options.MediaId.Value, cancellationToken);
            }
        }

        // Taken before any work is done for the call, so that calls sent together cannot all pass, and given back when the
        // call is refused before it is sent.
        if (limits.Take(site, operation.Principal, current.CallsPerMinute, out var ticket) is { } refusal)
        {
            throw OverLimit(site, operation.Principal, refusal);
        }

        AiReservation? reservation = null;
        try
        {
            // Whichever factory fills the slot: a host's own may not look at the provider, and none is off as FieldsAsync
            // says. The client comes before the budget, since the budget follows whose credential it calls with.
            if (AiProviders.IsRemoved(current.Provider))
            {
                throw AiProviders.Refuse(logger, site, current.Provider);
            }

            var client = (current.Provider == AiProvider.None ? null : await clients.CreateAsync(site, current, cancellationToken))
                ?? throw new PluginException(AiErrorCodes.NoProvider, "The site has no AI provider. An administrator names one in the AI settings of the site.");

            // Before the draft is read, a call is reckoned at the most text it may send; one that reads none sends only the
            // text it was given, if any. altText's documentId names the entry of the image, and its field is not read.
            var image = action == AiActions.AltText;
            var richText = field?.FieldType == RichTextFieldType;
            var draft = type is not null && field is not null && !image ? request.DocumentId : null;
            var instructions = AiActions.Instructions(action, type?.Name, field?.Name, richText, field?.MaxLength, locale, targetLocale);
            var sent = AiBudgets.Tokens(instructions) + (image ? AiBudgets.ImageTokens : 0);
            var reckoned = sent + (draft is null ? AiBudgets.Tokens(request.Text) : (long)options.Value.MaxInputLength * AiBudgets.MaxBytesPerChar);
            reservation = await ReserveAsync(site, current, client, reckoned + current.MaxOutputTokens, cancellationToken);

            var text = draft is { } id ? await ReadFieldAsync(type!, field!, id, locale, richText, cancellationToken) : request.Text;
            if (action != AiActions.AltText && string.IsNullOrWhiteSpace(text))
            {
                throw new ValidationException([Invalid("documentId", "The field of the draft holds no text.")]);
            }

            if (text?.Length > options.Value.MaxInputLength)
            {
                throw new PluginException(AiErrorCodes.InputTooLong, $"The text has more than the {options.Value.MaxInputLength} characters an AI action takes.");
            }

            List<AIContent> message = [];
            if (image)
            {
                message.Add(await ReadImageAsync(site, request.Options!.MediaId!.Value, cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                message.Add(new TextContent(text));
            }

            var prompt = sent + AiBudgets.Tokens(text);
            reservation?.Change(prompt + current.MaxOutputTokens);
            return new AiCall(
                action,
                site,
                operation.Principal,
                type?.Name ?? "",
                field?.Name ?? "",
                client,
                [
                    new ChatMessage(ChatRole.System, instructions),
                    new ChatMessage(ChatRole.User, message),
                ],
                new ChatOptions { MaxOutputTokens = current.MaxOutputTokens },
                TimeSpan.FromSeconds(current.RequestTimeout),
                prompt,
                reservation);
        }
        catch
        {
            reservation?.Release();
            AiCallLimits.GiveBack(ticket, current.CallsPerMinute);
            throw;
        }
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
        var produced = false;
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
                produced = text.Length > 0;
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
                        produced = true;
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
            Math.Max(used.InputTokenCount ?? 0, 0),
            Math.Max(used.OutputTokenCount ?? 0, 0),
            (long)time.GetElapsedTime(started).TotalMilliseconds);

        // A call with the operator's credential that ends before its provider reports what it used, or with a provider
        // that reports nothing, is counted at its estimate, so that a budget over that credential cannot be passed by
        // leaving before the usage arrives. A prompt is never empty, and an answer with text used tokens, so a count of 0
        // for them is no count. The answer still says what the provider reported.
        var counted = call.Client.SiteCredential
            ? info
            : info with
            {
                PromptTokens = used.InputTokenCount is > 0 and var prompt ? prompt : call.PromptEstimate,
                CompletionTokens = used.OutputTokenCount is > 0 and var completion ? completion
                    : used.OutputTokenCount is 0 && !produced ? 0
                    : call.Options.MaxOutputTokens ?? 0,
            };
        try
        {
            if (!await RecordAsync(call, counted, outcome, code) && call.Reservation is not null)
            {
                budgets.Unrecorded(call.Site, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), counted.PromptTokens + counted.CompletionTokens);
            }
        }
        finally
        {
            call.Reservation?.Release();
        }

        AiTelemetry.End(span, call.Action, call.Client.Provider, outcome, code);
        return refused is null ? new AiActionResult(suggestion!, info) : throw refused;
    }

    private PluginException OverLimit(SiteId site, Principal principal, AiCallRefusal refusal)
    {
        if (refusal.Report)
        {
            AiLog.CallLimitExceeded(logger, principal.Kind, principal.Id, refusal.Limit, site.Value, refusal.RetryAfterSeconds);
        }

        return new PluginException(AiErrorCodes.CallLimitExceeded, $"The site allows {refusal.Limit} AI calls a minute for each caller. Try again in {refusal.RetryAfterSeconds} seconds.");
    }

    // The budget of the site is the lower of its own and, for a call with the operator's credential, the ceiling of the
    // configuration. A call is refused when the tokens of the site's rows since midnight UTC and the estimates of its other
    // calls in flight on this instance reach it; otherwise it reserves its own estimate until its row is written.
    private async Task<AiReservation?> ReserveAsync(SiteId site, AiSettings current, AiClient client, long estimate, CancellationToken cancellationToken)
    {
        var budget = Lower(current.DailyTokenBudget, client.SiteCredential ? 0 : options.Value.DailyTokenBudget);
        if (budget <= 0)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var reservation = budgets.Reserve(site, day, estimate);
        long written;
        try
        {
            written = await usage.TokensSinceAsync(site.Value, today, cancellationToken);
        }
        catch
        {
            reservation.Release();
            throw;
        }

        var used = written + reservation.Before;
        if (used >= budget)
        {
            reservation.Release();
            var resets = today.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (budgets.ReportOverBudget(site, day))
            {
                AiLog.BudgetExceeded(logger, site.Value, budget, used, resets);
            }

            throw new PluginException(AiErrorCodes.BudgetExceeded, written + reservation.Unrecorded >= budget
                ? $"The site has used its daily budget of {budget} AI tokens. It resets at {resets}."
                : $"The AI calls the site has made today and those it has in flight reach its daily budget of {budget} AI tokens. Try again when they end; the budget resets at {resets}.");
        }

        return reservation;

        // 0 is no budget, so it is the lower of two only when both are 0.
        static int Lower(int first, int second) => first <= 0 ? second : second <= 0 ? first : Math.Min(first, second);
    }

    private static bool TakesActions(SchemaField field) =>
        TextFieldTypes.Contains(field.FieldType, StringComparer.Ordinal) && !field.Multiple && !field.Hidden && !field.ReadOnly;

    private static bool IsOptedIn(AiSettings settings, SchemaType type, SchemaField field) =>
        TakesActions(field) && settings.Fields.TryGetValue(type.Name, out var optedIn) && optedIn.Contains(field.Name, StringComparer.Ordinal);

    private static bool MayUpdate(ContentTypeAccess access, SchemaField field) => access.Update && access.UpdatableFields.Contains(field.Name);

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

    // altText for a field describes an image of its own entry: one that the draft refers to in the locale of the suggestion,
    // by a media field or a media node of a rich text field, among the fields the caller may read. The draft is read as the
    // caller, so an entry it may not read is not found, and an image in a field it may not read counts as none.
    private async Task RequireReferencedAsync(ContentAccess held, SchemaType type, Guid id, string locale, Guid mediaId, CancellationToken cancellationToken)
    {
        var fields = held.Schema.FindContentType(type.Name)?.Fields
            .Where(field => !field.Hidden && field.FieldType is MediaFieldType or RichTextFieldType)
            .ToList() ?? [];
        var read = new GetOptions
        {
            Status = QueryStatus.Draft,
            Locale = type.Localized ? locale : null,
            Fallback = LocaleFallback.None,
            Select = fields.Count > 0 ? [.. fields.Select(field => field.Name)] : ["id"],
        };
        var entry = await content.GetAsync(type.Name, id, read, cancellationToken) ?? throw new EntryNotFoundException(type.Name, id);
        if (!fields.Any(field => entry.TryGetValue(field.Name, out var value) && RefersTo(value, mediaId)))
        {
            throw new ValidationException([Invalid("options.mediaId", "altText for a field describes an image that the entry's draft refers to in the locale of the suggestion, by a media field or a media node of a rich text field.")]);
        }
    }

    // A media field reads as { "id", "type" }, or a list of them, and rich text as its JSON document.
    private static bool RefersTo(object? value, Guid mediaId) => value switch
    {
        ContentDocument media => media.TryGetValue("id", out var held) && held is Guid found && found == mediaId,
        IEnumerable<ContentDocument> many => many.Any(media => RefersTo(media, mediaId)),
        RichText document => ShowsMedia(document, mediaId),
        _ => false,
    };

    private static bool ShowsMedia(RichText document, Guid mediaId)
    {
        using var json = JsonDocument.Parse(document.Json, RichTextJson);
        return Shows(json.RootElement);

        bool Shows(JsonElement node) =>
            node.ValueKind == JsonValueKind.Object
            && ((node.TryGetProperty("type", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == MediaFieldType
                    && node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
                    && attrs.TryGetProperty("mediaId", out var named) && named.ValueKind == JsonValueKind.String && Guid.TryParse(named.GetString(), out var found) && found == mediaId)
                || (node.TryGetProperty("content", out var children) && children.ValueKind == JsonValueKind.Array && children.EnumerateArray().Any(Shows)));
    }

    private async Task<IMediaService> MediaAsync(SiteId site, CancellationToken cancellationToken)
    {
        var media = services.GetService<IMediaService>();
        if (media is null || !await plugins.IsEnabledAsync(MediaPlugin, site, cancellationToken))
        {
            throw new PluginException(AiErrorCodes.ImageNotAvailable, "The site has no media library, so there is no image to describe.");
        }

        return media;
    }

    // The check of PATCH /media/{id}, by the media service's own code, which writes nothing: an asset outside the caller's
    // folders answers as one that is not there.
    private async Task RequireUpdatableAsync(SiteId site, Guid id, CancellationToken cancellationToken) =>
        await (await MediaAsync(site, cancellationToken)).RequireUpdateAsync(id, cancellationToken);

    // What an editor needs to offer altText for an asset alone: the media library, and media.read and media.update in some
    // folder. Whether a given asset is one of them is learnt at the call.
    private async Task<bool> MayDescribeAssetsAsync(SiteId site, CancellationToken cancellationToken)
    {
        if (services.GetService<IMediaService>() is null || !await plugins.IsEnabledAsync(MediaPlugin, site, cancellationToken))
        {
            return false;
        }

        return (await access.HeldAsync([MediaRead, MediaUpdate], cancellationToken)).Count == 2;
    }

    // A reduced copy without metadata, as the caller: the media service refuses a caller without media.read there.
    private async Task<DataContent> ReadImageAsync(SiteId site, Guid id, CancellationToken cancellationToken)
    {
        var media = await MediaAsync(site, cancellationToken);
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
    // False when the row could not be written.
    private async Task<bool> RecordAsync(AiCall call, AiUsageInfo info, string outcome, string? code)
    {
        var written = true;
        using var bound = new CancellationTokenSource(UsageWriteTimeout, time);
        try
        {
            await usage.RecordAsync(new AiUsage
            {
                Id = Guid.CreateVersion7(),
                SiteId = call.Site.Value,
                PrincipalKind = AiUsageService.KindOf(call.Principal.Kind),
                PrincipalId = call.Principal.Id,
                PrincipalName = activity.ActorName(call.Principal.Name),
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
            }, bound.Token);
        }
        catch (Exception exception)
        {
            written = false;
            AiLog.UsageNotRecorded(logger, exception, call.Action, call.Principal.Kind, call.Principal.Id, call.Site.Value);
        }

        AiLog.Called(logger, call.Action, call.Type, call.Field, call.Principal.Kind, call.Principal.Id, call.Site.Value, info.Provider, info.Model, outcome, code, info.DurationMs, info.PromptTokens, info.CompletionTokens);
        return written;
    }

    // Not through the middle of a surrogate pair, whose lone half a database may refuse to store, as core's activity text cuts.
    internal static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)];

    // The caller's connection failed while a piece of the answer was written to it.
    private sealed class CallerGoneException(Exception inner) : Exception("The caller went away.", inner);
}
