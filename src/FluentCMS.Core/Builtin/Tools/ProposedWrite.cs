using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using DirectoryTarget = FluentCMS.Core.ChangeSets.ChangeSetTarget;

namespace FluentCMS.Core.Builtin.Tools;

// A schema write an agent proposes. The directory stores the new version and records its operation in the change set in one
// unit of work, so no version an agent writes exists outside a change set a person reviews (SCH-05, AUD-N1, CHG-01).
internal static class ProposedWrite
{
    public static async Task<ToolResult> RunAsync(
        ToolInvocation call,
        ChangeSetTarget target,
        Func<IContentTypeDirectory, DirectoryTarget, Task<Result<ContentTypeWrite>>> write,
        CancellationToken cancellationToken)
    {
        var services = call.Services;

        var written = await write(services.GetRequiredService<IContentTypeDirectory>(), target.ToDirectoryTarget());
        if (!written.IsSuccess)
        {
            return ToolResult.Failure(written.Errors, Hint(written.Errors));
        }

        // The set the write joined, read by its id: one submitted since still answers, in its new state, and a session's next set
        // opened since is not mistaken for it.
        var type = written.Value.ContentType;
        var set = await services.GetRequiredService<IChangeSetDirectory>().GetAsync(written.Value.ChangeSetId, cancellationToken);
        if (!set.IsSuccess)
        {
            return ToolResult.Failure(set.Errors);
        }

        // The set says what the write became: a revision of a type with nothing live is recorded as its create.
        var recorded = set.Value.Operations.Single(operation => operation.ItemKind == ItemKind.Schema && operation.ItemId == type.Id);
        var open = set.Value.State == ChangeSetState.Open;

        return ToolResult.Success(new JsonObject
        {
            ["contentType"] = ToolJson.ContentType(type),
            ["changeSet"] = ToolJson.ChangeSet(set.Value),
            ["next"] = open
                ? $"The {ToolJson.Name(recorded.Kind)} is in change set {set.Value.Id}, which stays open for more "
                    + "operations. Call submit_change_set when it is complete; checks run and a person reviews it before it is published."
                : $"The {ToolJson.Name(recorded.Kind)} is in change set {set.Value.Id}, which was submitted since; read it with "
                    + "get_change_set, and write further changes into a new set.",
        });
    }

    private static string? Hint(IReadOnlyList<Error> errors)
    {
        return errors.FirstOrDefault()?.Code switch
        {
            SchemaErrorCodes.VersionConflict => "The message says which version to revise; read it with get_content_type, then revise from it.",
            SchemaErrorCodes.ContentTypeNotFound => "Call list_content_types for the slugs of the site's content types.",
            _ => null,
        };
    }
}
