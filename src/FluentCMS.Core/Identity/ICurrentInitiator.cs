namespace FluentCMS.Core.Identity;

public interface ICurrentInitiator
{
    // Null when nobody is signed in or the host runs without a caller, such as a scheduled job.
    Initiator? Current { get; }
}
