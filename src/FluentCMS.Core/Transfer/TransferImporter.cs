using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Telemetry;
using FluentCMS.Events;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Transfer;

// An import into the site of the request (spec 4.14), whatever the site of the package. The body is kept in a temporary
// file for the length of the request only, because a zip is read from its end. Everything that refuses the whole package is
// asked before the first write and before the first line of the answer. Then the settings, the roles when asked, the
// contributors and the content are imported in that order, each entry in a transaction and a scope of its own, as the system
// under Origin.Import for the caller, so an import that stopped is finished by running it again.
internal sealed class TransferImporter(
    IOperationContextAccessor accessor,
    ManagementAuthority authority,
    SchemaModel schema,
    LocaleRegistry locales,
    SettingsService settings,
    RoleService roles,
    TransferContributors contributors,
    IDistributedLock locks,
    IServiceScopeFactory scopes,
    IActivityLog activity,
    ErrorCodeCatalogue codes,
    TransferOptions options,
    TimeProvider time,
    ILogger<TransferImporter> logger)
{
    // The correlation id of an import's operation, which every event of it carries. The webhooks plugin delivers an event of
    // Origin.Import only when its correlation id starts with the second, which an import started with webhooks=true has.
    public const string CorrelationPrefix = "import:";
    public const string WebhooksCorrelationPrefix = "import+webhooks:";

    // The sections of the report that are no content type, whose names are lower kebab-case.
    public const string SettingsSection = "@settings";
    public const string RolesSection = "@roles";
    public const string PluginsSection = "@plugins";
    private const int ProgressEvery = 100;

    public async Task<TransferImportRun> PrepareAsync(Stream body, long? declaredLength, TransferImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(request);

        var operation = accessor.Current ?? throw new InvalidOperationException("An import runs inside an operation; the entry point sets IOperationContextAccessor.Current.");
        await authority.RequireAsync(CorePermissions.TransferManage, operation.Site.Value, cancellationToken);

        if (declaredLength > options.MaxPackageSize)
        {
            throw TooLarge();
        }

        // One import of a site at a time, so that two runs never write the same entries at once.
        var held = await locks.TryAcquireAsync($"transfer.import.{operation.Site.Value:N}", TimeSpan.Zero, cancellationToken)
            ?? throw new ValidationException([new ValidationError("", TransferErrorCodes.ImportRunning, "An import into this site is running; try again when it has finished.")]);
        FileStream file;
        try
        {
            file = new FileStream(
            Path.Combine(Path.GetTempPath(), $"fluentcms-import-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }
        catch
        {
            await held.DisposeAsync();
            throw;
        }

        try
        {
            await CopyAsync(body, file, cancellationToken);
            file.Position = 0;
            var archive = await OpenAsync(file, cancellationToken);
            try
            {
                return await PlanAsync(operation, request, archive, file, held, cancellationToken);
            }
            catch
            {
                await archive.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await file.DisposeAsync();
            await held.DisposeAsync();
            throw;
        }
    }

    public async Task<TransferResult> RunAsync(TransferImportRun run, Func<object, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progress);

        using var span = FluentCmsTelemetry.Start("transfer", "import", null, run.Operation);
        var caller = run.Operation;
        var site = caller.Site;
        var correlation = (run.Request.Webhooks ? WebhooksCorrelationPrefix : CorrelationPrefix) + Guid.CreateVersion7().ToString("N");
        var siteLocales = await locales.GetAsync(site, cancellationToken);
        var import = new OperationContext(Principal.System, Origin.Import, site, siteLocales.Default, caller.ClientId, correlation, time.GetUtcNow())
        {
            Actor = caller.AuditPrincipal.IsSystem ? null : caller.AuditPrincipal,
        };

        TransferLog.ImportStarted(logger, site.Value, correlation, run.Request.DryRun);
        var report = new TransferReport(run.Request.DryRun);
        var previous = accessor.Current;
        accessor.Current = import;
        try
        {
            await ImportSettingsAsync(run, report, cancellationToken);
            await progress(new { section = SettingsSection, counts = report.Of(SettingsSection) }, cancellationToken);

            if (run.Request.Roles && run.Roles.Count > 0)
            {
                await ImportRolesAsync(run, report, cancellationToken);
                await progress(new { section = RolesSection, counts = report.Of(RolesSection) }, cancellationToken);
            }

            var provided = await ImportContributorsAsync(run, report, progress, cancellationToken);

            foreach (var type in run.Types)
            {
                await ImportTypeAsync(run, type, siteLocales, provided, report, progress, cancellationToken);
                await progress(new { section = type, counts = report.Of(type) }, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            TransferLog.ImportStopped(logger, exception, site.Value);
            throw;
        }
        finally
        {
            accessor.Current = previous;
        }

        var total = report.Total;
        TransferLog.ImportFinished(logger, site.Value, total.Created, total.Updated, total.Skipped, total.Conflicts, total.Errors);
        if (!run.Request.DryRun)
        {
            await WriteActivityAsync(run, import, total);
        }

        return report.ToResult();
    }

    private async Task<TransferImportRun> PlanAsync(OperationContext operation, TransferImportRequest request, ZipArchive archive, FileStream file, IDistributedLockHandle held, CancellationToken cancellationToken)
    {
        // A file of the package is never larger, unpacked, than the package may be.
        if (archive.Entries.FirstOrDefault(entry => entry.Length > options.MaxPackageSize) is { } large)
        {
            throw Invalid($"{large.FullName} of the package is larger, unpacked, than {options.MaxPackageSize.ToString(CultureInfo.InvariantCulture)} bytes.");
        }

        var manifest = await ReadJsonAsync<TransferManifest>(archive, TransferPackage.Manifest, cancellationToken)
            ?? throw Invalid($"The package has no {TransferPackage.Manifest}.");
        if (manifest.FormatVersion != TransferPackage.FormatVersion)
        {
            throw new ValidationException([new ValidationError("formatVersion", TransferErrorCodes.UnsupportedFormat, $"The package has the format {manifest.FormatVersion}; this instance reads the format {TransferPackage.FormatVersion}.")]);
        }

        var packageSchema = await ReadTextAsync(archive, TransferPackage.Schema, cancellationToken)
            ?? throw Invalid($"The package has no {TransferPackage.Schema}.");

        var inPackage = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith(TransferPackage.ContentFolder, StringComparison.Ordinal) && name.EndsWith(TransferPackage.LinesExtension, StringComparison.Ordinal))
            .Select(name => name[TransferPackage.ContentFolder.Length..^TransferPackage.LinesExtension.Length])
            .Order(StringComparer.Ordinal)
            .ToList();
        var unknown = request.Types?.Where(type => !inPackage.Contains(type, StringComparer.Ordinal)).ToList() ?? [];
        if (unknown.Count > 0)
        {
            throw new ValidationException([.. unknown.Select(type => new ValidationError("types", TransferErrorCodes.InvalidOption, $"The package has no content of the type '{type}'."))]);
        }

        var types = request.Types is { } asked ? [.. inPackage.Where(type => asked.Contains(type, StringComparer.Ordinal))] : inPackage;

        // A global type is every site's, so writing it needs transfer.manage for the instance, as writing it does (4.8). Its
        // file counts whatever size it declares, since only reading it tells what it holds.
        var global = types.Where(type => schema.FindContentType(type)?.Scope == ContentScope.Global).ToList();
        if (global.Count > 0 && !await authority.AllowsAsync(CorePermissions.TransferManage, null, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"The package has the global types {string.Join(", ", global)}, whose import needs the instance-wide permission {CorePermissions.TransferManage}; import the other types with types=.");
        }

        await CheckSizesAsync(archive, cancellationToken);

        var instance = SchemaSnapshot.Of(schema);
        List<ValidationError> errors;
        try
        {
            errors = manifest.SchemaHash == instance.Hash ? [] : SchemaCompatibility.Check(packageSchema, instance.Json, types);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or InvalidCastException)
        {
            throw Invalid($"{TransferPackage.Schema} of the package cannot be read: {exception.Message}");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // Read whole before any write, so that a package that cannot be read is refused before it changes anything.
        var settingsFile = await ReadJsonAsync<List<TransferSetting>>(archive, TransferPackage.Settings, cancellationToken) ?? [];
        var rolesFile = request.Roles ? await ReadJsonAsync<List<TransferRole>>(archive, TransferPackage.Roles, cancellationToken) ?? [] : [];
        if (settingsFile.Any(item => item is null || string.IsNullOrEmpty(item.Area)) || rolesFile.Any(item => item is null || item.Name is null || item.Grants is null || item.Grants.Any(grant => grant is null || grant.Permission is null)))
        {
            throw Invalid($"{TransferPackage.Settings} or {TransferPackage.Roles} of the package has an item without its name.");
        }

        var siteLocales = await locales.GetAsync(operation.Site, cancellationToken);
        var missing = manifest.Locales.Where(code => !siteLocales.Codes.Contains(code, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0 && !request.SkipMissingLocales)
        {
            throw new ValidationException([.. missing.Select(code => new ValidationError("locales", TransferErrorCodes.LocaleNotInSite, $"The site does not allow the locale '{code}' of the package; allow it, or import with missingLocales=skip."))]);
        }

        return new TransferImportRun(operation, request, manifest, types, settingsFile, rolesFile, archive, file, held);
    }

    private async Task ImportSettingsAsync(TransferImportRun run, TransferReport report, CancellationToken cancellationToken)
    {
        foreach (var document in run.Settings)
        {
            await ItemAsync(report, SettingsSection, document.Area, () => settings.ImportAsync(document.Area, run.Operation.Site, document.Value.GetRawText(), run.Request.DryRun, run.Operation, cancellationToken), cancellationToken);
        }
    }

    // A grant limited to the site of the package is limited to the site of the import.
    private async Task ImportRolesAsync(TransferImportRun run, TransferReport report, CancellationToken cancellationToken)
    {
        var site = run.Operation.Site.Value;
        var packageSite = run.Manifest.Site.Id;
        foreach (var role in run.Roles)
        {
            await ItemAsync(
                report,
                RolesSection,
                role.Id.ToString("D"),
                () =>
                {
                    List<PermissionGrant> grants = [];
                    foreach (var grant in role.Grants)
                    {
                        var constraints = PermissionConstraints.FromJson(grant.Constraints);
                        if (constraints.Sites is { } limited)
                        {
                            constraints = PermissionConstraints.FromJson(JsonSerializer.Serialize(new
                            {
                                sites = limited.Select(item => item.Value == packageSite ? site : item.Value),
                                constraints.Locales,
                                constraints.Fields,
                                constraints.RowRule,
                                constraints.Resources,
                            }, TransferPackage.Json));
                        }

                        grants.Add(new PermissionGrant(grant.Permission, constraints));
                    }

                    var info = new RoleInfo(role.Id, site, role.Name, role.Description, BuiltIn: false, role.RequireTwoFactor, grants);
                    return roles.ImportAsync(info, site, run.Request.DryRun, run.Operation, cancellationToken);
                },
                cancellationToken);
        }
    }

    // A folder of the package that no contributor enabled in the site reads is reported, and the rest goes on.
    private async Task<HashSet<(string Target, Guid Id)>> ImportContributorsAsync(TransferImportRun run, TransferReport report, Func<object, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        HashSet<(string Target, Guid Id)> provided = [];
        var folders = run.Archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith(TransferPackage.PluginsFolder, StringComparison.Ordinal))
            .Select(name => name[TransferPackage.PluginsFolder.Length..].Split('/')[0])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var enabled = await contributors.EnabledInAsync(run.Operation.Site, cancellationToken);
        foreach (var folder in folders.Where(folder => !enabled.Exists(contributor => contributor.Name == folder)))
        {
            report.Add(PluginsSection, folder, TransferOutcome.Error, TransferErrorCodes.NoContributor, $"No plugin enabled in this site reads the folder plugins/{folder}/ of the package.");
        }

        foreach (var contributor in enabled.Where(contributor => folders.Contains(contributor.Name, StringComparer.Ordinal)))
        {
            var context = new ImportContext(run, contributor.Name, report, provided);
            await contributor.Contributor.ImportAsync(context, cancellationToken);
            await progress(new { section = PluginsSection + "/" + contributor.Name }, cancellationToken);
        }

        return provided;
    }

    private async Task ImportTypeAsync(TransferImportRun run, string type, SiteLocales siteLocales, IReadOnlySet<(string Target, Guid Id)> provided, TransferReport report, Func<object, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        var localized = schema.FindContentType(type)?.Localized ?? false;
        var entry = run.Archive.GetEntry(TransferPackage.ContentPath(type))!;
        await using var stream = await PackageEntryStream.OpenAsync(entry, cancellationToken);
        using var reader = new StreamReader(stream);
        var number = 0;
        var done = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TransferEntry? read;
            try
            {
                read = JsonSerializer.Deserialize<TransferLine>(line, TransferPackage.Json)?.ToEntry();
            }
            catch (JsonException exception)
            {
                report.Add(type, $"line {number.ToString(CultureInfo.InvariantCulture)}", TransferOutcome.Error, TransferErrorCodes.InvalidItem, $"Line {number.ToString(CultureInfo.InvariantCulture)} is not an entry: {exception.Message}");
                continue;
            }

            if (read is null || read.Locales is null || read.Locales.Count == 0 || read.Locales.Any(locale => locale is null || (locale.Draft is null && locale.Published is null)))
            {
                report.Add(type, read?.Id.ToString("D") ?? $"line {number.ToString(CultureInfo.InvariantCulture)}", TransferOutcome.Error, TransferErrorCodes.InvalidItem, $"Line {number.ToString(CultureInfo.InvariantCulture)} has no locale with a draft or a published state.");
                continue;
            }

            var kept = read.Locales
                .Select(locale => localized ? Locale(locale, siteLocales) : locale)
                .OfType<TransferEntryLocale>()
                .ToList();
            var id = read.Id.ToString("D");
            if (kept.Count == 0)
            {
                report.Add(type, id, TransferOutcome.Skipped, TransferErrorCodes.LocaleNotInSite, "The site allows none of the locales of the entry.");
            }
            else
            {
                await ItemAsync(report, type, id, () => ImportEntryAsync(run, type, read with { Locales = kept }, provided, cancellationToken), cancellationToken);
            }

            if (++done % ProgressEvery == 0)
            {
                await progress(new { section = type, done }, cancellationToken);
            }
        }
    }

    // The code as the site lists it, or null for a locale the site does not allow, which the plan let through only to be skipped.
    private static TransferEntryLocale? Locale(TransferEntryLocale locale, SiteLocales siteLocales) =>
        siteLocales.Codes.FirstOrDefault(code => string.Equals(code, locale.Locale, StringComparison.OrdinalIgnoreCase)) is { } code
            ? locale with { Locale = code }
            : null;

    private async Task<TransferOutcome> ImportEntryAsync(TransferImportRun run, string type, TransferEntry entry, IReadOnlySet<(string Target, Guid Id)> provided, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var mode = scope.ServiceProvider.GetRequiredService<TransferImportMode>();
        mode.Active = true;
        mode.DryRun = run.Request.DryRun;
        mode.Provided = provided;
        return await scope.ServiceProvider.GetRequiredService<ContentService>().ImportAsync(type, entry, run.Request.Overwrite, cancellationToken);
    }

    // One item: what the instance refuses is reported with its code, and anything else is logged and reported too, so that
    // the rest of the package is imported. Only a cancelled request stops the run.
    private async Task ItemAsync(TransferReport report, string section, string id, Func<Task<TransferOutcome>> import, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await import();
            report.Add(section, id, outcome, null, outcome == TransferOutcome.Conflict ? "The instance has a newer copy; import with overwrite=true to take the package's." : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var problem = ProblemMapper.Map(exception, authenticated: true, codes);
            if (problem.Code == ApiErrorCodes.InternalError)
            {
                TransferLog.ItemFailed(logger, exception, TransferErrorCodes.ItemFailed, section, id);
                report.Add(section, id, TransferOutcome.Error, TransferErrorCodes.ItemFailed, ContentService.UnexpectedFailure);
                return;
            }

            var message = problem.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors.Select(error => error.Path.Length > 0 ? $"{error.Path}: {error.Message}" : error.Message))
                : problem.Detail;
            report.Add(section, id, TransferOutcome.Error, problem.Code, message);
        }
    }

    private async Task WriteActivityAsync(TransferImportRun run, OperationContext import, TransferCounts total)
    {
        try
        {
            var entry = new ActivityEntry("transfer.imported")
            {
                TargetType = "site",
                TargetId = import.Site.Value.ToString("D"),
                Details = new Dictionary<string, string?>
                {
                    ["packageSite"] = run.Manifest.Site.Id.ToString("D"),
                    ["created"] = total.Created.ToString(CultureInfo.InvariantCulture),
                    ["updated"] = total.Updated.ToString(CultureInfo.InvariantCulture),
                    ["skipped"] = total.Skipped.ToString(CultureInfo.InvariantCulture),
                    ["conflicts"] = total.Conflicts.ToString(CultureInfo.InvariantCulture),
                    ["errors"] = total.Errors.ToString(CultureInfo.InvariantCulture),
                    ["overwrite"] = run.Request.Overwrite ? "true" : "false",
                    ["webhooks"] = run.Request.Webhooks ? "true" : "false",
                },
            };
            await activity.WriteAsync(entry, EventSource.From(import), CancellationToken.None);
        }
        catch (Exception exception)
        {
            TransferLog.ActivityFailed(logger, exception, import.Site.Value);
        }
    }

    private async Task CopyAsync(Stream body, FileStream file, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > options.MaxPackageSize)
            {
                throw TooLarge();
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await file.FlushAsync(cancellationToken);
    }

    private static async Task<ZipArchive> OpenAsync(FileStream file, CancellationToken cancellationToken)
    {
        try
        {
            return await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw Invalid($"The body is not a zip: {exception.Message}");
        }
    }

    // Every file is read through PackageEntryStream, so none yields more than it declares. A stored file yields its compressed
    // size and a deflated one stops at its declared size, so only a file whose compressed size is the larger can hold more.
    // Each such file is read through here, at no more cost than the package, so that the package is refused before any write.
    private static async Task CheckSizesAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries.Where(entry => entry.CompressedLength > entry.Length))
        {
            try
            {
                await using var stream = await PackageEntryStream.OpenAsync(entry, cancellationToken);
                await stream.CopyToAsync(Stream.Null, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                throw Invalid($"{entry.FullName} of the package cannot be read: {exception.Message}");
            }
        }
    }

    private static async Task<string?> ReadTextAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        if (archive.GetEntry(path) is not { } entry)
        {
            return null;
        }

        await using var stream = await PackageEntryStream.OpenAsync(entry, cancellationToken);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(ZipArchive archive, string path, CancellationToken cancellationToken)
        where T : class
    {
        if (archive.GetEntry(path) is not { } entry)
        {
            return null;
        }

        try
        {
            await using var stream = await PackageEntryStream.OpenAsync(entry, cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, TransferPackage.Json, cancellationToken)
                ?? throw Invalid($"{path} of the package is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            throw Invalid($"{path} of the package cannot be read: {exception.Message}");
        }
    }

    private static ValidationException Invalid(string message) => new([new ValidationError("", TransferErrorCodes.InvalidPackage, message)]);

    private ValidationException TooLarge() =>
        new([new ValidationError("", TransferErrorCodes.PackageTooLarge, $"The package is larger than {options.MaxPackageSize.ToString(CultureInfo.InvariantCulture)} bytes, FluentCms:Transfer:MaxPackageSize.")]);

    private sealed class ImportContext(TransferImportRun run, string name, TransferReport report, HashSet<(string Target, Guid Id)> provided) : ITransferImport
    {
        private readonly string _folder = TransferPackage.PluginFolder(name);

        public SiteId Site => run.Operation.Site;

        public SiteId PackageSite => new(run.Manifest.Site.Id);

        public IReadOnlyDictionary<string, string> Options => run.Request.Options;

        public bool DryRun => run.Request.DryRun;

        public bool Overwrite => run.Request.Overwrite;

        public IReadOnlyList<string> Paths =>
        [
            .. run.Archive.Entries
                .Select(entry => entry.FullName)
                .Where(path => path.StartsWith(_folder, StringComparison.Ordinal) && path.Length > _folder.Length && !path.EndsWith('/'))
                .Select(path => path[_folder.Length..]),
        ];

        public async Task<bool> ReadAsync(string path, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(read);

            if (TransferPackage.CheckPath(path) is not { } checkedPath || run.Archive.GetEntry(_folder + checkedPath) is not { } entry)
            {
                return false;
            }

            await using var stream = await PackageEntryStream.OpenAsync(entry, cancellationToken);
            await read(stream, cancellationToken);
            return true;
        }

        public void Report(string section, string id, TransferOutcome outcome, string? code = null, string? message = null)
        {
            report.Add(section, id, outcome, code, message);
            if (outcome is TransferOutcome.Created or TransferOutcome.Updated or TransferOutcome.Skipped && Guid.TryParse(id, out var target))
            {
                provided.Add((section, target));
            }
        }
    }
}

internal sealed class TransferImportRun(
    OperationContext operation,
    TransferImportRequest request,
    TransferManifest manifest,
    IReadOnlyList<string> types,
    IReadOnlyList<TransferSetting> settings,
    IReadOnlyList<TransferRole> roles,
    ZipArchive archive,
    FileStream file,
    IDistributedLockHandle held) : IAsyncDisposable
{
    public IReadOnlyList<TransferSetting> Settings => settings;

    public IReadOnlyList<TransferRole> Roles => roles;

    public OperationContext Operation => operation;

    public TransferImportRequest Request => request;

    public TransferManifest Manifest => manifest;

    public IReadOnlyList<string> Types => types;

    public ZipArchive Archive => archive;

    public async ValueTask DisposeAsync()
    {
        await archive.DisposeAsync();
        await file.DisposeAsync();
        await held.DisposeAsync();
    }
}
