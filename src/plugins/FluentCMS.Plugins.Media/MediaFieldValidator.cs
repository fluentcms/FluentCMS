using System.Collections;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// Holds a media field with an "accept" setting to the types it lists, on every save and publish, as a tightened length
// holds a text. An asset that is not in the site's library is left to the reference check, which refuses a new one.
internal sealed class MediaFieldValidator(PluginDbContext<MediaPlugin> database, MediaLibraries libraries) : IFieldValidator<object>
{
    public async ValueTask ValidateAsync(object? value, ValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Field is not { FieldType: MediaFieldType.TypeName } field
            || !field.Settings.TryGetValue(FieldSettings.Accept, out var setting)
            || setting is not IEnumerable<string> items
            || items.ToList() is not { Count: > 0 } accept)
        {
            return;
        }

        var ids = Ids(value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var library = await libraries.OfAsync(context.Operation.Site, cancellationToken);
        var types = await database.Set<Asset>().AsNoTracking()
            .Where(asset => ids.Contains(asset.Id) && asset.SiteId == library.Value && asset.DeletedAt == null)
            .Select(asset => new { asset.Id, asset.MimeType })
            .ToDictionaryAsync(asset => asset.Id, asset => asset.MimeType, cancellationToken);

        foreach (var id in ids.Where(id => types.TryGetValue(id, out var type) && !MediaAccept.Matches(accept, type)))
        {
            context.AddError(MediaErrorCodes.NotAccepted, $"The asset {id:D} is of a type the field '{field.Name}' does not accept; it accepts {string.Join(", ", accept)}.");
        }
    }

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
