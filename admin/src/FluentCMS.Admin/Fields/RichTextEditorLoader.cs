using System.Diagnostics.CodeAnalysis;
using FluentCMS.Admin.Plugins;

namespace FluentCMS.Admin.Fields;

// The editor of rich text lives in FluentCMS.Admin.RichText, which the app lists as BlazorWebAssemblyLazyLoad and this
// assembly does not reference, so the first form with a rich text field loads it and finds the editor by name (spec 2.6,
// 4.7). An app without it shows rich text as JSON, read-only, as any field type without an editor.
internal static class RichTextEditorLoader
{
    public const string FieldType = "richText";
    public const string AssemblyName = "FluentCMS.Admin.RichText";
    public const string EditorName = "FluentCMS.Admin.RichText.RichTextFieldEditor";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "FluentCMS.Admin.RichText is not trimmable, so the app's partial trimming keeps the editor whole.")]
    public static async Task<Type> LoadAsync(IAdminAssemblyLoader loader, CancellationToken cancellationToken)
    {
        var assembly = await loader.LoadAsync(AssemblyName, cancellationToken);
        return assembly.GetType(EditorName, throwOnError: true)!;
    }
}
