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

    // Whether create answers null, as the module does for a value the editor cannot hold whole.
    public bool Refuses { get; set; }

    public bool Mac { get; set; }

    public bool Disposed { get; private set; }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "isMac")
        {
            return ValueTask.FromResult((TValue)(object)Mac);
        }

        identifier.ShouldBeCreate();
        Created = args;
        return ValueTask.FromResult(Refuses ? default! : (TValue)(object)editor);
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

    // What "flush" answers once, the change the module held back, and whether "setContent" takes a value.
    public string? Pending { get; set; }

    public bool HasPending { get; set; }

    public bool RefusesContent { get; set; }

    // What "kept" answers: the selected media or inline block; and what "insertMedia", "insertBlock" and "duplicateKept" answer.
    public RichTextFieldEditor.KeptNode? Kept { get; set; }

    public RichTextFieldEditor.KeptNode? Inserted { get; set; }

    // What "entryLink" answers: the entry the link at the selection goes to.
    public EntryReference? EntryLink { get; set; }

    public bool Disposed { get; private set; }

    public IEnumerable<object?[]> CallsOf(string name) => Calls.Where(call => call.Name == name).Select(call => call.Args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Enqueue((identifier, args ?? []));
        object? result = identifier switch
        {
            "run" or "unsetLink" or "setEntryLink" or "unsetEntryLink" or "updateKept" or "removeKept" => true,
            "kept" => Kept,
            "insertMedia" or "insertBlock" or "duplicateKept" => Inserted,
            "entryLink" => EntryLink,
            "setContent" => !RefusesContent,
            "flush" => Flush(),
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

    private RichTextFieldEditor.PendingChange Flush()
    {
        var pending = new RichTextFieldEditor.PendingChange(HasPending, HasPending ? Pending : null);
        HasPending = false;
        return pending;
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
