namespace FluentCMS.Admin.Components;

// Every control of the wrapped set needs an accessible name (spec 4.18). [EditorRequired] covers markup only, and generated
// forms build their inputs in code, so a missing name is refused when the component renders.
internal static class CmsGuard
{
    public static void Named(string? name, string component, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"A {component} needs a {parameter}, which names it for assistive technology.");
        }
    }
}
