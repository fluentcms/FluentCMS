using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Media;

// Every media storage registered through the extension point, by name. Built-ins and plugins arrive the same way (EXT-N1).
public sealed class MediaStorageRegistry
{
    private readonly Dictionary<string, IMediaStorage> _byName = new(StringComparer.Ordinal);

    public MediaStorageRegistry(IEnumerable<IMediaStorage> storages)
    {
        ArgumentNullException.ThrowIfNull(storages);

        // Every asset stores its storage's name, so a bad or duplicate one stops the application from starting.
        foreach (var storage in storages)
        {
            if (!SchemaNames.IsFieldTypeName(storage.Name))
            {
                throw new InvalidOperationException($"{storage.GetType()} is named '{storage.Name}', which is not a valid media storage name.");
            }

            if (!_byName.TryAdd(storage.Name, storage))
            {
                throw new InvalidOperationException($"{storage.GetType()} and {_byName[storage.Name].GetType()} are both named '{storage.Name}'.");
            }
        }
    }

    public IReadOnlyList<string> Names => _byName.Keys.Order(StringComparer.Ordinal).ToList();

    public IMediaStorage? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }
}
