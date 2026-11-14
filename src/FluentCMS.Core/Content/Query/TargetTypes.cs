using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// The content types of the relation targets and entry links that a scope's reads asked about, at every level, for a
// Delivery read whose tag header would pass its budget to carry their tags in place of those of its entries (4.10).
internal sealed class TargetTypes
{
    private readonly HashSet<SchemaType> _types = [];

    public IReadOnlyCollection<SchemaType> Types
    {
        get
        {
            lock (_types)
            {
                return [.. _types];
            }
        }
    }

    public void Add(SchemaType type)
    {
        lock (_types)
        {
            _types.Add(type);
        }
    }
}
