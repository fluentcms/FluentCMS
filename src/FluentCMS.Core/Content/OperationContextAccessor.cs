using FluentCMS.Content;

namespace FluentCMS.Core.Content;

// Flows with the async call, so that an entry point sets it once and a parallel request has its own.
internal sealed class OperationContextAccessor : IOperationContextAccessor
{
    private static readonly AsyncLocal<OperationContext?> Context = new();

    public OperationContext? Current
    {
        get => Context.Value;
        set => Context.Value = value;
    }
}
