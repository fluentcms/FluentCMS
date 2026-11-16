using Microsoft.AspNetCore.Components;
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
