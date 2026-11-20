using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Pickers;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin.Fields;

internal static class FieldServiceCollectionExtensions
{
    // The editors and list cells of the core field types (spec 4.6), and those of the app's admin halves, which load on first
    // use, and rich text, whose editor loads on first use too (A-24). Scoped, as the halves' are gated by the shell's site.
    public static IServiceCollection AddFieldEditors(this IServiceCollection services)
    {
        services.TryAddScoped(provider =>
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
            editors.Add<RelationFieldEditor>("relation");
            editors.Add<NestedFieldEditor>("component");
            editors.Add<NestedFieldEditor>("blocks");
            if (provider.GetService<IAdminAssemblyLoader>() is { } loader)
            {
                editors.AddLazy(RichTextEditorLoader.FieldType, () => true, cancellationToken => RichTextEditorLoader.LoadAsync(loader, cancellationToken));
            }

            provider.GetService<AdminPlugins>()?.Declare(editors);
            return editors;
        });
        services.TryAddSingleton(new BlockFormRegistration(typeof(BlockForm)));
        services.TryAddScoped<IRelationPicker, RelationPickerService>();
        services.TryAddScoped<BulkProgressService>();
        services.TryAddScoped<IBulkProgressService>(provider => provider.GetRequiredService<BulkProgressService>());
        services.TryAddScoped<PreviewInterop>();
        services.TryAddScoped<BulkRunner>();
        services.TryAddScoped(provider =>
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
            cells.Add<RelationListCell>("relation");
            provider.GetService<AdminPlugins>()?.Declare(cells);
            return cells;
        });
        services.TryAddScoped(provider =>
        {
            var filters = new ListFilterRegistry();
            provider.GetService<AdminPlugins>()?.Declare(filters);
            return filters;
        });
        return services;
    }
}
