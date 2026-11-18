using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Plugins;

// A form or a list loads the admin halves of its fields' types when it opens (spec 2.6), so that the registries' lookups
// answer them as they draw. A half that does not load leaves its field types as if no half provided them: a form shows the
// JSON read-only, and a list shows the value as text or no column.
internal static class PluginFieldTypes
{
    public static async Task LoadAsync(IEnumerable<SchemaFieldDescription> fields, Func<SchemaFieldDescription, Task> load)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(load);
        foreach (var field in fields.DistinctBy(field => field.Type))
        {
            try
            {
                await load(field);
            }
#pragma warning disable CA1031 // The failure is logged where the half loads; the view goes on without it.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
            }
        }
    }
}
