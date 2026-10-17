namespace FluentCMS.Core.Extensions;

internal sealed class ExtensionCatalog(IEnumerable<ExtensionDescriptor> descriptors) : IExtensionCatalog
{
    public IReadOnlyList<ExtensionDescriptor> All { get; } = descriptors.ToList();

    public IReadOnlyList<ExtensionDescriptor> For(Type contract)
    {
        return All.Where(descriptor => descriptor.Contract == contract).ToList();
    }
}
