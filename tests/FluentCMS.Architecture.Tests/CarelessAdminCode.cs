using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Architecture.Tests;

// What the interop rule must catch, outside the list of spec 3.1. They are never run.
internal sealed class CarelessInterop(IJSRuntime js)
{
    public ValueTask AlertAsync() => js.InvokeVoidAsync("alert", "saved");
}

// What @inject IJSInProcessRuntime JS compiles to in a .razor component.
internal sealed class CarelessComponent : ComponentBase
{
    [Inject]
    public IJSInProcessRuntime JS { get; set; } = default!;
}

internal sealed class CarelessModule(IJSObjectReference module)
{
    public ValueTask CopyAsync(string text) => module.InvokeVoidAsync("copy", text);
}

// The interop of the runtime itself, which [JSImport] builds on.
[SupportedOSPlatform("browser")]
internal static class CarelessHost
{
    public static Task<JSObject> ImportAsync() => JSHost.ImportAsync("careless", "./careless.js");
}

// What the rule on the component library must catch outside the wrapped set.
internal static class CarelessLibraryUser
{
    public static Type Button => typeof(FluentButton);
}
