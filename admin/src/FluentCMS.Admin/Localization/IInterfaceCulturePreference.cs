namespace FluentCMS.Admin.Localization;

// The user's choice of interface culture, read at start before the host is built. The preferences of #3096 come after
// sign-in, so their source keeps the last known choice for the next start.
internal interface IInterfaceCulturePreference
{
    string? Culture { get; }
}

internal sealed class NoInterfaceCulturePreference : IInterfaceCulturePreference
{
    public string? Culture => null;
}
