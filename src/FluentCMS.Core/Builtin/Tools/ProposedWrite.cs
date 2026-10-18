using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// A schema write an agent proposes: the new version and its operation in the change set commit together or not at all, so no
// version an agent writes exists outside a change set a person reviews (AUD-N1, CHG-01).
internal static class ProposedWrite
{
    public static async Task<ToolResult> RunAsync(
        ToolInvocation call,
        ChangeSetTarget target,
        Func<IContentTypeDirectory, Task<Result<ContentType>>> write,
        Func<ContentType, ChangeOperation> operation,
        CancellationToken cancellationToken)
    {
        var services = call.Services;
        var ready = await target.CheckAsync(services, cancellationToken);
        if (!ready.IsSuccess)
        {
            return ToolResult.Failure(ready.Errors);
        }

        // Each directory finds its expected failures before its own unit of work, so leaving this one uncommitted undoes both.
        await using var scope = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        var type = await write(services.GetRequiredService<IContentTypeDirectory>());
        if (!type.IsSuccess)
        {
            return ToolResult.Failure(type.Errors, Hint(type.Errors));
        }

        var set = await target.AddAsync(services.GetRequiredService<IChangeSetDirectory>(), operation(type.Value), cancellationToken);
        if (!set.IsSuccess)
        {
            return ToolResult.Failure(set.Errors);
        }

        await scope.CommitAsync(cancellationToken);

        return ToolResult.Success(new JsonObject
        {
            ["contentType"] = ToolJson.ContentType(type.Value),
            ["changeSet"] = ToolJson.ChangeSet(set.Value),
            ["next"] = $"The {ToolJson.Name(operation(type.Value).Kind)} is in change set {set.Value.Id}, which stays open for more "
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
