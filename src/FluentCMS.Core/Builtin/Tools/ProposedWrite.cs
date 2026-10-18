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
        Func<IContentTypeDirectory, DirectoryTarget, Task<Result<ContentType>>> write,
        OperationKind kind,
        CancellationToken cancellationToken)
    {
        var services = call.Services;

        var type = await write(services.GetRequiredService<IContentTypeDirectory>(), target.ToDirectoryTarget());
        if (!type.IsSuccess)
        {
            return ToolResult.Failure(type.Errors, Hint(type.Errors));
        }

        var set = await target.ReadAsync(services.GetRequiredService<IChangeSetDirectory>(), cancellationToken);
        if (!set.IsSuccess)
        {
            return ToolResult.Failure(set.Errors);
        }

        return ToolResult.Success(new JsonObject
        {
            ["contentType"] = ToolJson.ContentType(type.Value),
            ["changeSet"] = ToolJson.ChangeSet(set.Value),
            ["next"] = $"The {ToolJson.Name(kind)} is in change set {set.Value.Id}, which stays open for more "
                + "operations. Call submit_change_set when it is complete; checks run and a person reviews it before it is published.",
        });
    }
    private static string? Hint(IReadOnlyList<Error> errors)
    {
        return errors.FirstOrDefault()?.Code switch
        {
            SchemaErrorCodes.VersionConflict => "Call get_content_type for the current version and its fields, then revise from that version.",
            SchemaErrorCodes.ContentTypeNotFound => "Call list_content_types for the slugs of the site's content types.",
            _ => null,
        };
    }
}
