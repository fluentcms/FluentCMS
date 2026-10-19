using System.Text.Json;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Models;

// What a caller asks a model. The instructions are the caller's own, written in code; everything that came from content, rules,
// sources or agents goes in the data, which the prompt keeps apart from the instructions (SEC-03).
public sealed record ModelRequest(string Profile, string Purpose, string Instructions, IReadOnlyList<ModelData> Data)
{
    // A JSON Schema the answer must follow; the provider is asked for JSON in that shape.
    public JsonElement? AnswerSchema { get; init; }

    // Whose work an engine asks the model for, such as the agent or person whose change set it checks; only an engine names one.
    // An agent's call counts against its LLM spend cap (SEC-04), and a person's against none.
    public Initiator? OnBehalfOf { get; init; }
}

// One named section of untrusted text, such as "content" or "rule".
public sealed record ModelData(string Name, string Text);

// The answer and the record it was kept under, so the caller can cite the call, its model and its prompt (MDL-04, CHK-05).
public sealed record ModelAnswer(Guid CallId, string Provider, string Model, string Text, string PromptHash, decimal Cost);
