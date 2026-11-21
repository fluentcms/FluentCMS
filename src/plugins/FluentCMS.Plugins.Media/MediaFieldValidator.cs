using System.Collections;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// Holds a media field with an "accept" setting to the types it lists, on every save and publish, as a tightened length
// holds a text. An asset that is not in the site's library is left to the reference check, which refuses a new one. The
// types are read once a validation run, for the assets of every field of the run that has accept, and kept for that run
// only, so that a later run, such as the stored validation of a publish, reads them as they are then.
internal sealed class MediaFieldValidator(PluginDbContext<MediaPlugin> database, MediaLibraries libraries) : IFieldValidator<object>
{
    private IReadOnlyList<ValidatedField>? _run;
    private readonly HashSet<Guid> _read = [];
    private readonly Dictionary<Guid, string> _types = [];

    public async ValueTask ValidateAsync(object? value, ValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Field is not { } field || AcceptOf(field) is not { } accept)
        {
            return;
        }

        var ids = Ids(value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await ReadTypesAsync(context, ids, cancellationToken);
        foreach (var id in ids.Where(id => _types.TryGetValue(id, out var type) && !MediaAccept.Matches(accept, type)))
        {
            context.AddError(MediaErrorCodes.NotAccepted, $"The asset {id:D} is of a type the field '{field.Name}' does not accept; it accepts {string.Join(", ", accept)}.");
        }
    }

    // The first field of a run reads the types for every field of the run with accept; a later one reads only ids that were
    // not read, which a field the run does not list can bring. A context made outside a run lists no fields, so it is never
    // taken for the run before.
    private async Task ReadTypesAsync(ValidationContext context, List<Guid> ids, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> wanted = ids;
        if (context.Fields.Count == 0 || !ReferenceEquals(context.Fields, _run))
        {
            _run = context.Fields.Count == 0 ? null : context.Fields;
            _read.Clear();
            _types.Clear();
            wanted = ids.Concat(context.Fields.Where(other => AcceptOf(other.Field) is not null).SelectMany(other => Ids(other.Value)));
        }

        var unread = wanted.Where(_read.Add).ToList();
        if (unread.Count == 0)
        {
            return;
        }

        var library = await libraries.OfAsync(context.Operation.Site, cancellationToken);
        var types = await database.Set<Asset>().AsNoTracking()
            .Where(asset => unread.Contains(asset.Id) && asset.SiteId == library.Value && asset.DeletedAt == null)
            .Select(asset => new { asset.Id, asset.MimeType })
            .ToListAsync(cancellationToken);
        foreach (var asset in types)
        {
            _types[asset.Id] = asset.MimeType;
        }
    }

    private static List<string>? AcceptOf(SchemaField field) =>
        field is { FieldType: MediaFieldType.TypeName }
        && field.Settings.TryGetValue(FieldSettings.Accept, out var setting)
        && setting is IEnumerable<string> items
        && items.ToList() is { Count: > 0 } accept
            ? accept
            : null;

    // The value as the pipeline gives it: an id or a list of them, or the MediaRef and MediaRefList of a typed entry.
    private static IEnumerable<Guid> Ids(object? value) => value switch
    {
        Guid id => [id],
        MediaRef media => [media.Id],
        string or null => [],
        IEnumerable list => list.Cast<object?>().SelectMany(Ids),
        _ => [],
    };
}
