using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin.Fields;

internal static class FieldServiceCollectionExtensions
{
    // The editors of the core field types. Until A-14 only text has one; every other type shows its JSON read-only.
    public static IServiceCollection AddFieldEditors(this IServiceCollection services)
    {
        services.TryAddSingleton(_ =>
        {
            var editors = new FieldEditorRegistry();
            editors.Add<TextFieldEditor>("text");
            return editors;
        });
        return services;
    }
}
