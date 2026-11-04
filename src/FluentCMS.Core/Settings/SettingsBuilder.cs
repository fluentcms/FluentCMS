using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Settings;

// cms.Settings: the settings classes of the host, and how long a reader may keep what it read.
public sealed class SettingsBuilder
{
    private readonly List<Type> _types = [];

    // How long a read is kept in this process. A write on this instance drops what is kept at once; a write on another
    // instance is seen when this time has passed, so it is also the longest another instance can lag. Zero turns the cache off.
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    // T needs a [Settings("area")] attribute; the permission settings.{area}.manage joins the permission catalogue.
    public SettingsBuilder Add<T>()
        where T : class, new()
    {
        _types.Add(typeof(T));
        return this;
    }

    internal SettingsRegistry Build()
    {
        if (CacheDuration < TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(SettingsErrorCodes.InvalidRegistration, $"{nameof(CacheDuration)} must not be negative.");
        }

        return new SettingsRegistry(_types, CacheDuration);
    }
}
