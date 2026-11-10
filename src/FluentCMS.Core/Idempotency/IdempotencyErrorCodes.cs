namespace FluentCMS.Core.Idempotency;

// The codes of the Idempotency-Key a create or a bulk request may carry.
internal static class IdempotencyErrorCodes
{
    public const string InvalidKey = "FCMS3140";
    public const string KeyReused = "FCMS3141";
    public const string InProgress = "FCMS3142";
}
