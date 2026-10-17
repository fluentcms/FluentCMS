namespace FluentCMS.Core.Extensions;

// Marks an interface as a seam that built-in features and, in M3, plugins implement the same way (EXT-01, EXT-02).
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ExtensionPointAttribute : Attribute;
