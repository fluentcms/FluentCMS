using FluentCMS.Content;

namespace FluentCMS.Jobs;

/// <summary>Who a job acts for, to record on what it writes. It says nothing about what the job may do.</summary>
/// <param name="Kind">The kind of identity. <see cref="PrincipalKind.System"/> and <see cref="PrincipalKind.Anonymous"/> are refused.</param>
/// <param name="Id">The id of the identity.</param>
/// <param name="Name">A display name for logs and the activity log.</param>
public sealed record JobActor(PrincipalKind Kind, Guid Id, string Name);

/// <summary>
/// A job payload that names an actor. The job still runs as <see cref="Principal.System"/>: the actor reaches
/// <see cref="OperationContext.Actor"/> with no claims and is never used for authorization.
/// </summary>
public interface IJobActorPayload
{
    /// <summary>The actor to record, or <see langword="null"/> for none.</summary>
    JobActor? Actor { get; }
}
