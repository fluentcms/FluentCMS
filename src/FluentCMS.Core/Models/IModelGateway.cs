using FluentCMS.Core.Results;

namespace FluentCMS.Core.Models;

// The one way to a model (ADR-0008, MDL-01). Every call goes through a named profile, as a structured prompt (SEC-03), within the
// charged agent's spend cap (SEC-04) and to an allow-listed endpoint (SEC-05), and is recorded with its prompt and response (MDL-04).
public interface IModelGateway
{
    // Whether the profile is configured and its endpoint allowed. Without a model the governance loop still runs: callers degrade,
    // such as an LLM-judged check that needs review instead of passing or failing (MDL-03).
    bool IsAvailable(string profile);

    Task<Result<ModelAnswer>> AskAsync(ModelRequest request, CancellationToken cancellationToken = default);
}
