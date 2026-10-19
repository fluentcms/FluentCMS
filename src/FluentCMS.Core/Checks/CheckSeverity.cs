namespace FluentCMS.Core.Checks;

// What a failure of the check does to a change set: block it, or warn and let it go on (RUL-03, CHK-03).
public enum CheckSeverity
{
    Block,
    Warn,
}
