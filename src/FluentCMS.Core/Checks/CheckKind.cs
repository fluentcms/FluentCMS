namespace FluentCMS.Core.Checks;

// Deterministic checks run on the request path for small sets; LLM-judged ones need a model and run as a job (ADR-0008, ADR-0010).
public enum CheckKind
{
    Deterministic,
    LlmJudged,
}
