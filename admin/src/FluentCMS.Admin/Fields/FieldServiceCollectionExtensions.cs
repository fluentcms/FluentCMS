using FluentCMS.Admin.Lists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin.Fields;

internal static class FieldServiceCollectionExtensions
{
    // The editors and list cells of the core field types (spec 4.6). Relation, rich text, component, blocks and media wait for
    // their issues and show their JSON read-only in a form; a relation's cell is the label TextListCell shows.
    public static IServiceCollection AddFieldEditors(this IServiceCollection services)
    {
        services.TryAddSingleton(_ =>
        {
            var editors = new FieldEditorRegistry();
            editors.Add<TextFieldEditor>("text");
            editors.Add<TextFieldEditor>("email");
            editors.Add<TextFieldEditor>("url");
            editors.Add<LongTextFieldEditor>("longText");
            editors.Add<NumberFieldEditor>("integer");
            editors.Add<NumberFieldEditor>("decimal");
            editors.Add<BooleanFieldEditor>("boolean");
            editors.Add<DateFieldEditor>("date");
            editors.Add<DateTimeFieldEditor>("dateTime");
            editors.Add<SelectFieldEditor>("select");
            editors.Add<SlugFieldEditor>("slug");
            editors.Add<JsonFieldEditor>("json");
            return editors;
        });
        services.TryAddSingleton(_ =>
        {
            var cells = new ListCellRegistry();
            cells.Add<TextValueListCell>("text");
            cells.Add<TextValueListCell>("longText");
            cells.Add<TextValueListCell>("slug");
            cells.Add<TextValueListCell>("email");
            cells.Add<TextValueListCell>("url");
            cells.Add<NumberListCell>("integer");
            cells.Add<NumberListCell>("decimal");
            cells.Add<BooleanListCell>("boolean");
            cells.Add<DateListCell>("date");
            cells.Add<DateTimeListCell>("dateTime");
            cells.Add<SelectListCell>("select");
            cells.Add<JsonListCell>("json");
            return cells;
        });
        return services;
    }
}
