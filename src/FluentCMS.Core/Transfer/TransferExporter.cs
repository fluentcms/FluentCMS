using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Telemetry;
using FluentCMS.Events;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Transfer;

// An export of the site of the request (spec 4.14), streamed as it is written: nothing is stored, and a caller that goes
// away cancels it. It needs transfer.manage in the site and reads as the system, so the package is whole whatever the
// caller may read. One export of a site runs at a time, across every instance.
internal sealed class TransferExporter(
    IOperationContextAccessor accessor,
    ManagementAuthority authority,
    SchemaModel schema,
    EntryExport entries,
    SettingsService settings,
    RoleService roles,
    SiteManager sites,
    LocaleRegistry locales,
    TransferContributors contributors,
    IDistributedLock locks,
    IActivityLog activity,
    TimeProvider time,
    ILogger<TransferExporter> logger)
{
    // Everything that can refuse the export is asked before the first byte of the answer.
    public async Task<TransferExportRun> PrepareAsync(TransferExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = accessor.Current ?? throw new InvalidOperationException("An export runs inside an operation; the entry point sets IOperationContextAccessor.Current.");
        await authority.RequireAsync(CorePermissions.TransferManage, operation.Site.Value, cancellationToken);

        List<ValidationError> errors = [];
        foreach (var name in request.Types ?? [])
        {
            if (schema.FindContentType(name) is null)
            {
                errors.Add(new ValidationError("types", TransferErrorCodes.InvalidOption, $"'{name}' is not a content type of this instance."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var types = schema.ContentTypes
            .Where(type => request.Types is null ? request.GlobalTypes || type.Scope == ContentScope.Site : request.Types.Contains(type.Name, StringComparer.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
        var site = await sites.FindInfoAsync(operation.Site.Value, cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {operation.Site}.")]);
        var enabled = await contributors.EnabledInAsync(operation.Site, cancellationToken);

        var held = await locks.TryAcquireAsync($"transfer.export.{operation.Site.Value:N}", TimeSpan.Zero, cancellationToken)
            ?? throw new ValidationException([new ValidationError("", TransferErrorCodes.ExportRunning, "An export of this site is running; try again when it has finished.")]);
        return new TransferExportRun(operation, request, site, types, enabled, held);
    }

    public async Task WriteAsync(TransferExportRun run, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(output);

        await using var held = run.Lock;
        using var span = FluentCmsTelemetry.Start("transfer", "export", null, run.Operation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, held.Lost);
        var token = stop.Token;
        var site = new SiteId(run.Site.Id);
        TransferLog.ExportStarted(logger, site.Value);

        var previous = accessor.Current;
        accessor.Current = run.Operation with { Principal = Principal.System, Actor = run.Operation.AuditPrincipal };
        var count = 0;
        try
        {
            await using var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, token);
            var snapshot = SchemaSnapshot.Of(schema);
            List<string> sections = [TransferPackage.Schema, .. run.Types.Select(type => TransferPackage.ContentPath(type.Name)), TransferPackage.Settings];
            if (run.Request.Roles)
            {
                sections.Add(TransferPackage.Roles);
            }

            sections.AddRange(run.Contributors.Select(contributor => TransferPackage.PluginFolder(contributor.Name)));
            var manifest = new TransferManifest(
                TransferPackage.FormatVersion,
                Version,
                snapshot.Hash,
                new TransferSite(run.Site.Id, run.Site.Slug, run.Site.Name, run.Site.DefaultLocale),
                (await locales.GetAsync(site, token)).Codes,
                time.GetUtcNow(),
                run.Request,
                sections);

            await WriteFileAsync(archive, TransferPackage.Manifest, stream => JsonSerializer.SerializeAsync(stream, manifest, TransferPackage.Json, token), token);
            await WriteFileAsync(archive, TransferPackage.Schema, stream => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(snapshot.Json), token).AsTask(), token);

            foreach (var type in run.Types)
            {
                await WriteFileAsync(
                    archive,
                    TransferPackage.ContentPath(type.Name),
                    async stream =>
                    {
                        await foreach (var entry in entries.ReadAsync(type, site, run.Request.PublishedOnly, token))
                        {
                            await JsonSerializer.SerializeAsync(stream, TransferLine.Of(entry), TransferPackage.Json, token);
                            await stream.WriteAsync(TransferPackage.NewLine, token);
                            count++;
                        }
                    },
                    token);
            }

            var documents = await settings.SiteDocumentsAsync(site, token);
            List<TransferSetting> settingsFile = [.. documents.Select(document => new TransferSetting(document.Area, JsonDocument.Parse(document.Json).RootElement.Clone()))];
            await WriteFileAsync(archive, TransferPackage.Settings, stream => JsonSerializer.SerializeAsync(stream, settingsFile, TransferPackage.Json, token), token);

            if (run.Request.Roles)
            {
                var siteRoles = await roles.SiteRolesAsync(site.Value, token);
                List<TransferRole> rolesFile = [.. siteRoles.Select(role => new TransferRole(role.Id, role.Name, role.Description, role.RequireTwoFactor, [.. role.Grants.Select(grant => new TransferGrant(grant.Permission, grant.Constraints.ToJson()))]))];
                await WriteFileAsync(archive, TransferPackage.Roles, stream => JsonSerializer.SerializeAsync(stream, rolesFile, TransferPackage.Json, token), token);
            }

            foreach (var contributor in run.Contributors)
            {
                await contributor.Contributor.ExportAsync(new ExportContext(archive, contributor.Name, site, run.Request.Options ?? new Dictionary<string, string>()), token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            TransferLog.ExportStopped(logger, exception, site.Value);
            throw;
        }
        finally
        {
            accessor.Current = previous;
        }

        TransferLog.ExportFinished(logger, site.Value, count);
        await WriteActivityAsync(run, count);
    }

    private static string Version =>
        typeof(TransferExporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    internal static async Task WriteFileAsync(ZipArchive archive, string path, Func<Stream, Task> write, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await write(stream);
    }

    private async Task WriteActivityAsync(TransferExportRun run, int count)
    {
        try
        {
            var entry = new ActivityEntry("transfer.exported")
            {
                TargetType = "site",
                TargetId = run.Site.Id.ToString("D"),
                TargetLabel = run.Site.Slug,
                Details = new Dictionary<string, string?>
                {
                    ["entries"] = count.ToString(CultureInfo.InvariantCulture),
                    ["types"] = string.Join(",", run.Types.Select(type => type.Name)),
                    ["publishedOnly"] = run.Request.PublishedOnly ? "true" : "false",
                    ["roles"] = run.Request.Roles ? "true" : "false",
                },
            };
            await activity.WriteAsync(entry, EventSource.From(run.Operation), CancellationToken.None);
        }
        catch (Exception exception)
        {
            TransferLog.ActivityFailed(logger, exception, run.Site.Id);
        }
    }

    private sealed class ExportContext(ZipArchive archive, string name, SiteId site, IReadOnlyDictionary<string, string> options) : ITransferExport
    {
        public SiteId Site => site;

        public IReadOnlyDictionary<string, string> Options => options;

        public Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(write);

            var checkedPath = TransferPackage.CheckPath(path)
                ?? throw new ArgumentException($"'{path}' is not a relative path inside the folder of the contributor.", nameof(path));
            return WriteFileAsync(archive, TransferPackage.PluginFolder(name) + checkedPath, stream => write(stream, cancellationToken), cancellationToken);
        }
    }
}

internal sealed record TransferExportRun(
    OperationContext Operation,
    TransferExportRequest Request,
    SiteInfo Site,
    IReadOnlyList<SchemaType> Types,
    IReadOnlyList<TransferContributor> Contributors,
    IDistributedLockHandle Lock);
