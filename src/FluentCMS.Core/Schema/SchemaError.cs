namespace FluentCMS.Core.Schema;

public sealed record SchemaError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}
