using FluentCMS.Content;
using FluentCMS.Plugins;

namespace FluentCMS.Core.Api.Common;

// The problem the REST APIs would answer an exception with, for a plugin surface that answers in a form of its own.
internal sealed class ProblemDescriber(IOperationContextAccessor operations, ErrorCodeCatalogue codes) : IProblemDescriber
{
    public ProblemDescription Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var problem = ProblemMapper.Map(exception, operations.Current is { Principal.IsAnonymous: false }, codes);
        return new ProblemDescription(problem.Code, problem.Status, problem.Title, problem.Detail)
        {
            Errors = [.. (problem.Errors ?? []).Select(error => new ValidationError(error.Path, error.Code, error.Message))],
        };
    }
}
