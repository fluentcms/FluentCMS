namespace FluentCMS.Core.Extensions;

public interface IExtensionCatalog
{
    IReadOnlyList<ExtensionDescriptor> All { get; }

    IReadOnlyList<ExtensionDescriptor> For(Type contract);

    IReadOnlyList<ExtensionDescriptor> For<TContract>()
    {
        return For(typeof(TContract));
    }
}
