using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Media (MED-01 to MED-03): agents upload assets with their alt text and caption, describe them, read and list them, and attach them
// to fields. Every write goes into a change set a person reviews.
public sealed class MediaTools : IToolProvider
{
    private const string FolderExample = "products/drills";
    private const string TagExample = "product-shot";
    private const string AltTextExample = "A cordless drill on a workbench.";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "upload_media",
            "Upload a media asset",
            "Uploads a file, given as base64, with its content type and metadata: it stores the bytes and version 1, unpublished, and "
            + "adds the create to a change set in the same call. Give alt text for every image: it describes the image to people "
            + "who cannot see it, and a check fails pages that show an image without it. Attach it to a field with attach_media.",
            ToolSchemas.Object([
                ToolSchemas.String("content_base64", "The file's bytes in base64.", "iVBORw0KGgo="),
                ToolSchemas.String("content_type", "The file's media type.", "image/png"),
                ToolSchemas.String("file_name", "The file's name, without folders.", "drill.png"),
                .. MetadataArguments,
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            UploadAsync),
        new AgentTool(
            "update_media",
            "Propose new metadata for a media asset",
            "Proposes new metadata for an asset from the version you read, given as base_version: it stores the next version with the "
            + "same bytes and adds the update to a change set in the same call. Each property you leave out keeps its value.",
            ToolSchemas.Object([
                IdArgument,
                ToolSchemas.PositiveInteger("base_version", "The latest version you read, as get_media returned it.", 1),
                ToolSchemas.String("file_name", "The file's new name.", "drill.png", required: false),
                .. MetadataArguments,
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            UpdateAsync),
        new AgentTool(
            "get_media",
            "Get a media asset",
            "Returns one asset with its metadata, content type, size and hash, for its latest version or the version you name, "
            + "and its latest and published versions.",
            ToolSchemas.Object(IdArgument, ToolSchemas.PositiveInteger("version", "A version to read instead of the latest.", 1, required: false)),
            readOnly: true,
            GetAsync),
        new AgentTool(
            "list_media",
            "List media assets",
            "Lists the assets in a folder, with or without its subfolders, or with a tag, oldest first, each with its file name, "
            + "type, size, folder, tags and whether it has alt text. Without arguments it lists every asset.",
            ToolSchemas.Object(
                ToolSchemas.String("folder", "A folder, such as products/drills; / is the root.", FolderExample, required: false),
                ToolSchemas.Boolean("include_subfolders", "With folder: also list the assets in its subfolders.", required: false),
                ToolSchemas.String("tag", "Only assets with this tag.", TagExample, required: false)),
            readOnly: true,
            ListAsync),
        new AgentTool(
            "attach_media",
            "Propose attaching media to an entry",
            "Proposes showing an asset in a media field of an entry, from the version of the entry you read as base_version: a single "
            + "media field is set to the asset, and a list of media gets it added. It stores the entry's next version and adds the "
            + "change to a change set in the same call.",
            ToolSchemas.Object([
                ToolSchemas.Uuid("entry_id", "The entry, as list_entries or create_entry returned it."),
                ToolSchemas.PositiveInteger("base_version", "The entry's latest version you read, as get_entry returned it.", 1),
                ToolSchemas.String("field", "The entry's media field, such as hero.", "hero"),
                ToolSchemas.Uuid("asset_id", "The asset to show, as upload_media or list_media returned it."),
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            AttachAsync),
    ];

    private static ToolArgument IdArgument { get; } = ToolSchemas.Uuid("id", "The asset's id, as upload_media or list_media returned it.");

    private static ToolArgument[] MetadataArguments { get; } =
    [
        ToolSchemas.String("folder", "The folder it sits in: lowercase words joined by hyphens, with slashes between levels.", FolderExample, required: false),
        ToolSchemas.Strings("tags", "Tags of lowercase words joined by hyphens.", TagExample, required: false),
        ToolSchemas.String("alt_text", "What the image shows, for people who cannot see it.", AltTextExample, required: false),
        ToolSchemas.String("caption", "A caption pages show with it.", "Our best-selling drill.", required: false),
    ];

    private static async Task<ToolResult> UploadAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var content = arguments.RequiredString("content_base64", "iVBORw0KGgo=");
        var contentType = arguments.RequiredString("content_type", "image/png");
        var fileName = arguments.RequiredString("file_name", "drill.png");
        var folder = arguments.OptionalString("folder", FolderExample);
        var tags = arguments.OptionalStrings("tags", TagExample);
        var altText = arguments.OptionalString("alt_text", AltTextExample);
        var caption = arguments.OptionalString("caption", "Our best-selling drill.");
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());

        var bytes = content is null ? null : Base64(content, errors);
        var metadata = fileName is null ? null : AssetMetadata.Create(fileName, folder, tags, altText, caption);
        errors.AddRange(metadata?.Errors ?? []);
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposeAsync(
            call,
            target!,
            AgentOperation.Create,
            library => library.UploadAsync(new NewAsset(new MemoryStream(bytes!), contentType!, metadata!.Value), cancellationToken),
            asset => ChangeOperation.Create(OperationKind.Create, ItemKind.Media, asset.Id, null, asset.LatestVersion),
            uploaded: true,
            cancellationToken);
    }

    private static async Task<ToolResult> UpdateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var fileName = arguments.OptionalString("file_name", "drill.png");
        var folder = arguments.OptionalString("folder", FolderExample);
        var tags = arguments.OptionalStrings("tags", TagExample);
        var altText = arguments.OptionalString("alt_text", AltTextExample);
        var caption = arguments.OptionalString("caption", "Our best-selling drill.");
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposeAsync(
            call,
            target!,
            AgentOperation.Update,
            async library =>
            {
                var read = await library.GetVersionAsync(id!.Value, baseVersion!.Value, cancellationToken);
                if (!read.IsSuccess)
                {
                    return Result<MediaAsset>.Failure(read.Errors);
                }

                var current = read.Value.Metadata;
                var metadata = AssetMetadata.Create(
                    fileName ?? current.FileName,
                    folder ?? current.Folder,
                    tags ?? current.Tags,
                    altText ?? current.AltText,
                    caption ?? current.Caption);

                return metadata.IsSuccess
                    ? await library.UpdateAsync(id.Value, baseVersion.Value, metadata.Value, cancellationToken)
                    : Result<MediaAsset>.Failure(metadata.Errors);
            },
            asset => asset.PublishedVersion is { } published
                ? ChangeOperation.Create(OperationKind.Update, ItemKind.Media, asset.Id, published, asset.LatestVersion)
                : ChangeOperation.Create(OperationKind.Create, ItemKind.Media, asset.Id, null, asset.LatestVersion),
            uploaded: false,
            cancellationToken);
    }

    private static async Task<ToolResult> GetAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var version = arguments.OptionalPositiveInteger("version");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var readable = await call.Services.GetRequiredService<IAgentPermissions>().CheckMediaAsync(AgentOperation.Read, cancellationToken);
        if (!readable.IsSuccess)
        {
            return ToolResult.Failure(readable.Errors);
        }

        var library = call.Services.GetRequiredService<IMediaLibrary>();
        var asset = await library.GetAsync(id!.Value, cancellationToken);
        if (!asset.IsSuccess || version is not { } number)
        {
            return ToolResult.From(asset, read => ToolJson.Media(read), Hint(asset.Errors));
        }

        var stored = await library.GetVersionAsync(id.Value, number, cancellationToken);
        return ToolResult.From(stored, read => ToolJson.Media(asset.Value, read), "Leave out the version to read the latest.");
    }

    private static async Task<ToolResult> ListAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var folder = arguments.OptionalString("folder", FolderExample);
        var subfolders = arguments.OptionalFlag("include_subfolders");
        var tag = arguments.OptionalString("tag", TagExample);

        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var readable = await call.Services.GetRequiredService<IAgentPermissions>().CheckMediaAsync(AgentOperation.Read, cancellationToken);
        if (!readable.IsSuccess)
        {
            return ToolResult.Failure(readable.Errors);
        }

        var assets = await call.Services.GetRequiredService<IMediaLibrary>().ListAsync(new MediaQuery(folder, subfolders ?? false, tag), cancellationToken);
        return ToolResult.Success(new JsonObject { ["media"] = new JsonArray([.. assets.Select(ToolJson.MediaSummary)]) });
    }

    private static async Task<ToolResult> AttachAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var entryId = arguments.RequiredGuid("entry_id");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var field = arguments.RequiredString("field", "hero");
        var assetId = arguments.RequiredGuid("asset_id");
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposedEntryWrite.RunAsync(
            call,
            target!,
            entries => AttachToEntryAsync(call.Services, entries, entryId!.Value, baseVersion!.Value, field!, assetId!.Value, cancellationToken),
            ProposedEntryWrite.Update,
            cancellationToken);
    }

    // Sets a single media field, or adds to a list of media, in a copy of the version the agent read; on an entity the value sits
    // inside the field's fact, whose metadata stays.
    private static async Task<Result<Entry>> AttachToEntryAsync(
        IServiceProvider services,
        IEntryDirectory entries,
        Guid entryId,
        int baseVersion,
        string field,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var entry = await entries.GetAsync(entryId, cancellationToken);
        if (!entry.IsSuccess)
        {
            return entry;
        }

        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(entry.Value.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<Entry>.Failure(type.Errors);
        }

        var definition = type.Value.Definition;
        var media = definition.Fields.FirstOrDefault(candidate => candidate.Name == field);
        if (media is null || !IsMediaField(media))
        {
            return Result<Entry>.Failure(
                MediaErrorCodes.FieldNotMedia,
                $"'{field}' is not a media field of '{entry.Value.ContentType}'; its media fields are "
                + string.Join(", ", definition.Fields.Where(IsMediaField).Select(candidate => candidate.Name))
                + ".");
        }

        var isList = media.Type == "list";

        var read = await entries.GetVersionAsync(entryId, baseVersion, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<Entry>.Failure(read.Errors);
        }

        var fields = JsonNode.Parse(read.Value.Fields.GetRawText())!.AsObject();
        var holder = definition.IsEntity
            ? fields[field] as JsonObject ?? new JsonObject
            {
                [FactMetadata.Value] = null,
                [FactMetadata.Source] = null,
                [FactMetadata.Owner] = null,
                [FactMetadata.ValidFrom] = null,
                [FactMetadata.ValidUntil] = null,
                [FactMetadata.LastVerified] = null,
            }
            : fields;
        var key = definition.IsEntity ? FactMetadata.Value : field;
        var asset = assetId.ToString();

        if (isList)
        {
            var list = holder[key] as JsonArray ?? [];
            if (!list.Any(item => item is JsonValue value && value.TryGetValue<string>(out var existing) && Guid.TryParse(existing, out var parsed) && parsed == assetId))
            {
                list.Add(asset);
            }

            holder[key] = list.DeepClone();
        }
        else
        {
            holder[key] = asset;
        }

        if (definition.IsEntity)
        {
            fields[field] = holder.DeepClone();
        }

        return await entries.UpdateAsync(entryId, baseVersion, JsonSerializer.SerializeToElement(fields), cancellationToken);
    }

    // A single media field or a list of media.
    private static bool IsMediaField(FieldDefinition field)
    {
        return field.Type == "media"
            || (field.Type == "list" && field.Settings.TryGetProperty("itemType", out var itemType) && itemType.GetString() == "media");
    }

    // The version an agent writes and its operation commit together or not at all; uploaded bytes that end up in no asset are deleted
    // again, so a refused upload leaves nothing in the storage.
    private static async Task<ToolResult> ProposeAsync(
        ToolInvocation call,
        ChangeSetTarget target,
        AgentOperation agentOperation,
        Func<IMediaLibrary, Task<Result<MediaAsset>>> write,
        Func<MediaAsset, Result<ChangeOperation>> operation,
        bool uploaded,
        CancellationToken cancellationToken)
    {
        var services = call.Services;
        var ready = await target.CheckAsync(services, cancellationToken);
        if (!ready.IsSuccess)
        {
            return ToolResult.Failure(ready.Errors);
        }

        var allowed = await services.GetRequiredService<IAgentPermissions>().CheckMediaAsync(agentOperation, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return ToolResult.Failure(allowed.Errors);
        }

        MediaAsset? written = null;
        var committed = false;
        try
        {
            await using var scope = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

            var asset = await write(services.GetRequiredService<IMediaLibrary>());
            if (!asset.IsSuccess)
            {
                return ToolResult.Failure(asset.Errors, Hint(asset.Errors));
            }

            written = asset.Value;
            var proposed = operation(asset.Value);
            if (!proposed.IsSuccess)
            {
                return ToolResult.Failure(proposed.Errors);
            }

            var set = await target.AddAsync(services.GetRequiredService<IChangeSetDirectory>(), proposed.Value, cancellationToken);
            if (!set.IsSuccess)
            {
                return ToolResult.Failure(set.Errors);
            }

            await scope.CommitAsync(cancellationToken);
            committed = true;

            return ToolResult.Success(new JsonObject
            {
                ["media"] = ToolJson.Media(asset.Value),
                ["changeSet"] = ToolJson.ChangeSet(set.Value),
                ["next"] = $"The {ToolJson.Name(proposed.Value.Kind)} is in change set {set.Value.Id}. Attach the asset to a field with "
                    + "attach_media, and call submit_change_set when the set is complete.",
            });
        }
        finally
        {
            if (uploaded && !committed && written is { } orphan
                && services.GetRequiredService<MediaStorageRegistry>().Find(orphan.Latest.Blob.Storage) is { } storage)
            {
                await storage.DeleteAsync(orphan.Latest.Blob.Key, CancellationToken.None);
            }
        }
    }

    private static byte[]? Base64(string content, List<Error> errors)
    {
        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException)
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, "'content_base64' must be the file's bytes in base64."));
            return null;
        }
    }

    private static string? Hint(IReadOnlyList<Error> errors)
    {
        return errors.FirstOrDefault()?.Code switch
        {
            MediaErrorCodes.VersionConflict => "Call get_media for the latest version and its metadata, then update from that version.",
            MediaErrorCodes.AssetNotFound => "Call list_media for the ids of the site's assets.",
            MediaErrorCodes.MediaTooLarge => "Upload a smaller file; the administrator sets the limit with Media__MaxBytes.",
            _ => null,
        };
    }
}
