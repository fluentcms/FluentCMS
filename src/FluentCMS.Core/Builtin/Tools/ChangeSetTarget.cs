using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using DirectoryTarget = FluentCMS.Core.ChangeSets.ChangeSetTarget;

namespace FluentCMS.Core.Builtin.Tools;

// Where a write tool records its operation (CHG-01, CHG-02): an open change set named by its id, or the caller's set for a
// session id, which the session's first operation opens with the call's rationale and sources.
internal sealed class ChangeSetTarget
{
    private const string SessionExample = "catalog-sync-2026-10-18";
    private const string RationaleExample = "Add a price field so product pages can show prices.";
    private const string SourceExample = "https://example.com/catalog-spec";

    private readonly Guid? _changeSetId;
    private readonly string? _sessionId;
    private readonly NewChangeSet? _opening;

    private ChangeSetTarget(Guid? changeSetId, string? sessionId, NewChangeSet? opening)
    {
        _changeSetId = changeSetId;
        _sessionId = sessionId;
        _opening = opening;
    }

    public static ToolArgument[] Arguments { get; } =
    [
        ToolSchemas.Uuid(
            "change_set_id",
            "The open change set to add the operation to, as open_change_set returned it. Give this or session_id, not both.",
            required: false),
        ToolSchemas.String(
            "session_id",
            "Groups your operations into one change set without keeping its id: the first operation of a session opens a set, "
            + "and later ones join it until it is submitted. Give this or change_set_id, not both.",
            SessionExample,
            required: false),
        ToolSchemas.String(
            "rationale",
            "Why the change is made, for the person who reviews it. Required with session_id, where it opens the session's set.",
            RationaleExample,
            required: false),
        ToolSchemas.Strings(
            "sources",
            "Where the change comes from, such as URLs or file names, for the reviewer. Only with session_id.",
            SourceExample,
            required: false),
    ];

    public static ChangeSetTarget? Read(ToolArgumentsReader arguments, List<Error> errors)
    {
        var changeSetId = arguments.OptionalGuid("change_set_id");
        var sessionId = arguments.OptionalString("session_id", SessionExample);
        var rationale = arguments.OptionalString("rationale", RationaleExample);
        var sources = arguments.OptionalStrings("sources", SourceExample);

        if (changeSetId is not null && sessionId is null && rationale is null && sources is null)
        {
            return new ChangeSetTarget(changeSetId, null, null);
        }

        if (sessionId is not null && changeSetId is null && rationale is not null)
        {
            return new ChangeSetTarget(null, sessionId, new NewChangeSet(rationale, sources ?? []));
        }

        // A malformed id is already reported by the reader; saying the target is missing as well would mislead.
        if (changeSetId is null && sessionId is null && arguments.Has("change_set_id"))
        {
            return null;
        }

        errors.Add(new Error(
            ToolErrorCodes.ArgumentMissing,
            changeSetId is not null
                ? "With 'change_set_id' give neither 'session_id', 'rationale' nor 'sources': the open set already has its rationale."
                : sessionId is not null
                    ? "'rationale' is required with 'session_id': it opens the session's change set and the reviewer reads it."
                    : "Say which change set the operation goes into: give 'change_set_id' from open_change_set, or a 'session_id' with a 'rationale'."));
        return null;
    }

    // For a write whose directory does not join the change set itself, such as an entry's: refuses ahead of the write what the
    // change set would refuse after it, so no version is stored for nothing and a refused cap keeps its security event (SEC-08),
    // which a rolled-back write would take with it. The set checks again on the add.
    public async Task<Result> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var directory = services.GetRequiredService<IChangeSetDirectory>();
        var permissions = services.GetRequiredService<IAgentPermissions>();

        var set = _changeSetId is { } id
            ? await directory.GetAsync(id, cancellationToken)
            : await directory.GetSessionSetAsync(_sessionId!, cancellationToken);

        if (set.IsSuccess)
        {
            return await permissions.CheckChangeSetSizeAsync(set.Value, adding: 1, cancellationToken);
        }

        return _changeSetId is null ? await permissions.CheckNewChangeSetAsync(cancellationToken) : Result.Failure(set.Errors);
    }

    public Task<Result<ChangeSet>> AddAsync(IChangeSetDirectory directory, ChangeOperation operation, CancellationToken cancellationToken)
    {
        return _changeSetId is { } id
            ? directory.AddOperationAsync(id, operation, cancellationToken)
            : directory.AddToSessionAsync(_sessionId!, _opening!, operation, cancellationToken);
    }

    // The directories take the same target; they find every refusal, a cap included, before they write anything (SCH-05).
    public DirectoryTarget ToDirectoryTarget()
    {
        return _changeSetId is { } id ? DirectoryTarget.Existing(id) : DirectoryTarget.Session(_sessionId!, _opening!);
    }

    // The set the write went into, read after it to answer the call.
    public Task<Result<ChangeSet>> ReadAsync(IChangeSetDirectory directory, CancellationToken cancellationToken)
    {
        return _changeSetId is { } id ? directory.GetAsync(id, cancellationToken) : directory.GetSessionSetAsync(_sessionId!, cancellationToken);
    }
}
