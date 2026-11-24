using FluentCMS.Admin.Actions;

namespace FluentCMS.Admin.Plugins.Sample;

// Field actions the half does not declare: a test's own manifest names them, so that the sample's manifest stays as its
// attributes say.
public sealed class ClearFieldAction : FieldActionBase
{
    public override string Label => "Clear";

    public override string? ConfirmationText(FieldActionContext context) => $"Clear {context.Field.Name}?";

    public override Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(FieldActionResult.Replace(null));
}

public sealed class ThrowingFieldAction : FieldActionBase
{
    public override string Label => "Throw";

    public override Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The sample's field action failed.");
}

// Offered on no field, whatever its value.
public sealed class NeverFieldAction : FieldActionBase
{
    public override string Label => "Never";

    public override ValueTask<bool> AppliesToAsync(FieldActionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public override Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(FieldActionResult.Unchanged);
}

// Gives an object, which no text field takes.
public sealed class ObjectFieldAction : FieldActionBase
{
    public override string Label => "Object";

    public override Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(FieldActionResult.Replace(new System.Text.Json.Nodes.JsonObject { ["a"] = 1 }));
}

// Answers when the test releases it, whatever its token says, with a text for the field.
public sealed class HeldFieldAction : FieldActionBase
{
    public static TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Set as the action answers.
    public static TaskCompletionSource Answered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override string Label => "Held";

    public override async Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        await Release.Task;
        Answered.TrySetResult();
        return FieldActionResult.Replace(System.Text.Json.Nodes.JsonValue.Create("late"));
    }
}

// Throws ObjectDisposedException when the test releases it, as an action that used something of a form that went would.
public sealed class DisposedFieldAction : FieldActionBase
{
    public static TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Set as the action throws.
    public static TaskCompletionSource Thrown { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override string Label => "Disposed";

    public override async Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        await Release.Task;
        Thrown.TrySetResult();
        throw new ObjectDisposedException("form");
    }
}
