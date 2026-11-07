using System.Reflection;
using System.Text.RegularExpressions;
using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Plugins;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// What every plugin of the host contributed that core reads after the plugins have run: field type names and error codes,
// which must be unique across core and the plugins, and the tables of each plugin in plugin order.
internal sealed class PluginContributions
{
    private static readonly Regex CodePattern = new("^[A-Z]{2,}[0-9]{4}\\z", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string> _fieldTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ErrorCodeInfo Info, string Plugin)> _errorCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tables = new(StringComparer.Ordinal);
    private readonly List<PluginStorage> _storage = [];

    public IReadOnlyDictionary<string, ErrorCodeInfo> ErrorCodes => _errorCodes.ToDictionary(item => item.Key, item => item.Value.Info, StringComparer.Ordinal);

    public IReadOnlyList<PluginStorage> Storage => _storage;

    public PluginModels Models => PluginModels.Of(_storage.SelectMany(storage => storage.Contributors));

    public void AddFieldType(RegisteredPlugin plugin, IFieldType fieldType)
    {
        var owner = FieldTypeRegistry.Core.Find(fieldType.Name) is not null ? "core" : _fieldTypes.GetValueOrDefault(fieldType.Name) is { } other ? $"the plugin '{other}'" : null;
        if (owner is not null)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.DuplicateFieldType,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) adds the field type '{fieldType.Name}' ({fieldType.GetType().FullName}), which {owner} already has.");
        }

        // Introspection leaves a reference out for a caller who cannot read its field, and a schema without a required setting
        // would not load again.
        if (fieldType.Settings.FirstOrDefault(setting => setting is { Kind: FieldSettingKind.FieldReference, Required: true }) is { } required)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.RequiredFieldReference,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) adds the field type '{fieldType.Name}', whose field reference '{required.Key}' is required; a field reference is optional, as a slug's source is.");
        }

        _fieldTypes[fieldType.Name] = plugin.Name;
    }

    public void AddErrorCode(RegisteredPlugin plugin, string code, int status, string title)
    {
        var problem = code is null || !CodePattern.IsMatch(code) ? "is not upper case letters followed by four digits, such as ACME0001"
            : ErrorCodeRegistry.Find(code) is not null ? "is a code of core"
            : _errorCodes.TryGetValue(code, out var taken) ? $"is a code of the plugin '{taken.Plugin}'"
            : status is < 400 or > 599 ? $"has the status {status}, and a code has one from 400 to 599"
            : string.IsNullOrWhiteSpace(title) ? "has no title"
            : null;
        if (problem is not null)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidErrorCode,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) adds the error code '{code}', which {problem}.");
        }

        _errorCodes[code!] = (new ErrorCodeInfo(code!, status, title), plugin.Name);
    }

    public void AddModel(RegisteredPlugin plugin, IPluginModelContributor contributor)
    {
        var names = StorageOf(plugin).AddModel(contributor, name =>
            StorageNames.CoreTables.Contains(name) ? "core"
            : _tables.GetValueOrDefault(name) is { } other && other != plugin.Name ? $"the plugin '{other}'"
            : null);
        foreach (var name in names)
        {
            _tables.TryAdd(name, plugin.Name);
        }
    }

    // Schema validation reserves the system tables but cannot know the plugins'.
    public void CheckContentTables(SchemaModel schema)
    {
        foreach (var type in schema.ContentTypes)
        {
            var table = StorageNames.Table(type);
            var locale = StorageNames.LocaleTable(type);
            if ((_tables.GetValueOrDefault(table) ?? (type.Localized ? _tables.GetValueOrDefault(locale) : null)) is { } plugin)
            {
                throw new FluentCmsConfigurationException(
                    SchemaErrorCodes.ReservedName,
                    $"The content type '{type.Name}' needs a table that the plugin '{plugin}' maps, {(_tables.ContainsKey(table) ? table : locale)}.");
            }
        }
    }

    public PluginStorage StorageOf(RegisteredPlugin plugin)
    {
        var storage = _storage.Find(item => item.Plugin == plugin);
        if (storage is null)
        {
            storage = new PluginStorage(plugin);
            _storage.Add(storage);
        }

        return storage;
    }
}

internal sealed class PluginServiceBuilder(FluentCmsBuilder cms, RegisteredPlugin plugin, PluginContributions contributions)
    : IPluginServiceBuilder, IPluginStorageBuilder
{
    public string Name => plugin.Name;

    public IServiceCollection Services => cms.Services;

    public IPluginServiceBuilder AddFieldType<TFieldType>()
        where TFieldType : class, IFieldType, new()
    {
        var fieldType = new TFieldType();
        contributions.AddFieldType(plugin, fieldType);
        cms.Services.AddSingleton<IFieldType>(fieldType);
        return this;
    }

    public IPluginServiceBuilder AddPermissions<TProvider>()
        where TProvider : class, IPermissionProvider
    {
        cms.Services.AddSingleton<IPermissionProvider, TProvider>();
        return this;
    }

    public IPluginServiceBuilder AddSettings<TSettings>()
        where TSettings : class, new()
    {
        cms.Settings.Add<TSettings>();
        return this;
    }

    public IPluginServiceBuilder AddJob<TJob, TPayload>(string type, TimeSpan? attemptTimeout = null)
        where TJob : class, IJob<TPayload>
    {
        cms.Jobs.AddJob<TJob, TPayload>(type, attemptTimeout);
        return this;
    }

    public IPluginServiceBuilder AddEventHandler<TEvent, THandler>()
        where TEvent : CmsEvent
        where THandler : class, IEventHandler<TEvent>
    {
        cms.Events.AddHandler<TEvent, THandler>();
        return this;
    }

    public IPluginServiceBuilder AddOutboxHandler<TEvent, THandler>(string name)
        where TEvent : CmsEvent
        where THandler : class, IOutboxEventHandler<TEvent>
    {
        cms.Events.AddOutboxHandler<TEvent, THandler>(name);
        return this;
    }

    public IPluginServiceBuilder AddActivity<TEvent>(string name, Func<TEvent, ActivityEntry> describe)
        where TEvent : CmsEvent
    {
        cms.Events.AddActivity(name, describe);
        return this;
    }

    public IPluginServiceBuilder AddErrorCode(string code, int status, string title)
    {
        contributions.AddErrorCode(plugin, code, status, title);
        return this;
    }

    public void AddModel(IPluginModelContributor contributor) => contributions.AddModel(plugin, contributor);

    public void AddMigrations(Assembly assembly) => contributions.StorageOf(plugin).AddMigrations(assembly);
}

internal sealed class PluginAppBuilder(RegisteredPlugin plugin, IServiceProvider services, RouteGroupBuilder cms)
    : IPluginAppBuilder, IPluginEndpointBuilder
{
    public string Name => plugin.Name;

    public IServiceProvider Services => services;

    public RouteGroupBuilder MapGroup(string prefix) => FluentCmsEndpointRouteBuilderExtensions.MapPluginGroup(cms, plugin, prefix);

    public RouteGroupBuilder MapManagement(string prefix) => FluentCmsEndpointRouteBuilderExtensions.MapPluginManagement(cms, plugin, prefix);
}
