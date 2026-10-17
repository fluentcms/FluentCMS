namespace FluentCMS.Core.Extensions;

public sealed record ExtensionDescriptor(Type Contract, Type Implementation)
{
    public const string BuiltInNamespace = "FluentCMS.Core.Builtin";

    // Built-ins live under FluentCMS.Core.Builtin and register exactly like everything else (ADR-0004).
    public bool IsBuiltIn =>
        Implementation.Namespace is { } ns
        && (ns == BuiltInNamespace || ns.StartsWith(BuiltInNamespace + ".", StringComparison.Ordinal));
}
