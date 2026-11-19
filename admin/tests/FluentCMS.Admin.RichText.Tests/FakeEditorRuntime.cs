using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.RichText.Tests;

// The browser's side of the editor: the module's import, its create, and the calls on the editor it answers, each recorded.
// A call of the component library's own is answered with its default.
internal sealed class FakeEditorRuntime : IJSRuntime
{
    public FakeEditor Editor { get; } = new();

    public FakeModule Module { get; }

    public Exception? ImportFails { get; set; }

    public FakeEditorRuntime() => Module = new FakeModule(Editor);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "import" && args?[0] as string == RichTextFieldEditor.ModulePath)
        {
            return ImportFails is { } failure ? ValueTask.FromException<TValue>(failure) : ValueTask.FromResult((TValue)(object)Module);
        }

        return ValueTask.FromResult(typeof(TValue) == typeof(IJSObjectReference) ? (TValue)(object)new FakeEditor() : default!);
    }
}

internal sealed class FakeModule(FakeEditor editor) : IJSObjectReference
{
    public object?[]? Created { get; private set; }

    public bool Disposed { get; private set; }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        identifier.ShouldBeCreate();
        Created = args;
        return ValueTask.FromResult((TValue)(object)editor);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeEditor : IJSObjectReference
{
    public ConcurrentQueue<(string Name, object?[] Args)> Calls { get; } = new();

    // What "link" answers: the href of the link at the selection.
    public string? Link { get; set; }

    public bool Disposed { get; private set; }

    public IEnumerable<object?[]> CallsOf(string name) => Calls.Where(call => call.Name == name).Select(call => call.Args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Enqueue((identifier, args ?? []));
        object? result = identifier switch
        {
            "run" or "unsetLink" => true,
            "link" => Link,
            // The module's own rule, which its schema test covers: here only the scheme of the test's unsafe link.
            "setLink" => args?[0] is string href && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase),
            _ => null,
        };
        return ValueTask.FromResult(result is null ? default! : (TValue)result);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class CallChecks
{
    public static void ShouldBeCreate(this string identifier)
    {
        if (identifier != "create")
        {
            throw new InvalidOperationException($"The module was called with {identifier}.");
        }
    }
}
