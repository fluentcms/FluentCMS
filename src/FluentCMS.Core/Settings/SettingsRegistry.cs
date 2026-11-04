using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Settings;

namespace FluentCMS.Core.Settings;

// The settings classes of the host, found once at startup so that a bad one stops the host and not a request.
internal sealed partial class SettingsRegistry
{
    public const int AreaMaxLength = 64;

    private readonly FrozenDictionary<string, SettingsDescriptor> _byArea;
    private readonly FrozenDictionary<Type, SettingsDescriptor> _byType;

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();

    public SettingsRegistry(IReadOnlyList<Type> types, TimeSpan cacheDuration)
    {
        CacheDuration = cacheDuration;
        Dictionary<string, SettingsDescriptor> byArea = new(StringComparer.Ordinal);
        Dictionary<Type, SettingsDescriptor> byType = [];

        foreach (var type in types)
        {
            if (type.GetCustomAttribute<SettingsAttribute>(inherit: false) is not { } attribute)
            {
                throw Invalid($"{type.FullName} is added as a settings class but has no [Settings(\"area\")] attribute.");
            }

            if (attribute.Area is not { Length: > 0 and <= AreaMaxLength } area || !AreaPattern().IsMatch(area))
            {
                throw Invalid($"The settings area of {type.FullName} is '{attribute.Area}'; an area is lower case letters and digits joined by single hyphens, up to {AreaMaxLength} characters.");
            }

            if (byType.ContainsKey(type))
            {
                throw Invalid($"{type.FullName} is added as a settings class twice.");
            }

            if (byArea.TryGetValue(area, out var other))
            {
                throw Invalid($"{type.FullName} and {other.Type.FullName} both use the settings area '{area}'.");
            }

            var descriptor = new SettingsDescriptor(area, type);
            if (descriptor.KnownKeys.Count == 0)
            {
                throw Invalid($"{type.FullName} has no public read-write property, so there is nothing to set.");
            }

            byArea.Add(area, descriptor);
            byType.Add(type, descriptor);
        }

        _byArea = byArea.ToFrozenDictionary(StringComparer.Ordinal);
        _byType = byType.ToFrozenDictionary();
        Descriptors = [.. byArea.Values.OrderBy(descriptor => descriptor.Area, StringComparer.Ordinal)];
    }

    public TimeSpan CacheDuration { get; }

    public IReadOnlyList<SettingsDescriptor> Descriptors { get; }

    public SettingsDescriptor? Find(string area) => _byArea.GetValueOrDefault(area);

    public SettingsDescriptor For(Type type) =>
        _byType.GetValueOrDefault(type)
        ?? throw new FluentCmsConfigurationException(
            SettingsErrorCodes.InvalidRegistration,
            $"{type.FullName} is not a settings class of this host; add it with cms.Settings.Add<{type.Name}>() inside AddFluentCms.");

    private static FluentCmsConfigurationException Invalid(string message) => new(SettingsErrorCodes.InvalidRegistration, message);
}
