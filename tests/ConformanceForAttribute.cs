namespace FluentCMS.Testing;

// Names the extension points a conformance test class checks, so every extension point is seen to have one (EXT-N1). The check reads
// it from the source, because the classes live in more than one test project; both projects compile this one file.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConformanceForAttribute(params Type[] extensionPoints) : Attribute
{
    public IReadOnlyList<Type> ExtensionPoints { get; } = extensionPoints;
}
