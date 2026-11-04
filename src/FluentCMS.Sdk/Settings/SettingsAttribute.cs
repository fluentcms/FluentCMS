namespace FluentCMS.Settings;

/// <summary>
/// Marks a class as a settings area that administrators change at runtime, per site. The class needs a public
/// parameterless constructor, whose property values are the code defaults, and public read-write properties.
/// The permission to change it is <c>settings.{area}.manage</c>.
/// </summary>
/// <param name="area">The name of the area: lower case letters and digits joined by single hyphens, up to 64 characters, such as <c>media</c> or <c>site-seo</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SettingsAttribute(string area) : Attribute
{
    /// <summary>The name of the area. It is the key of the stored row and the middle of the permission.</summary>
    public string Area { get; } = area;
}
