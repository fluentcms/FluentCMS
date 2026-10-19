namespace FluentCMS.Core.Models;

// A named way to reach a model, such as "checks" or "risk-scoring", which the owner points at a provider (ADR-0008). Callers ask
// for a profile, never for a provider, so the owner can move a profile to another provider without a change to code.
public sealed record ModelProfile(string Name, string Provider, Uri Endpoint, string Model)
{
    // In US dollars per million tokens, the unit providers price in and agent spend caps count (SEC-04).
    public decimal InputCostPerMillionTokens { get; init; }

    public decimal OutputCostPerMillionTokens { get; init; }

    public int MaxOutputTokens { get; init; } = 1024;

    // Null leaves the provider's default, which some models require.
    public float? Temperature { get; init; }

    public decimal Cost(long inputTokens, long outputTokens)
    {
        return Math.Round(((inputTokens * InputCostPerMillionTokens) + (outputTokens * OutputCostPerMillionTokens)) / 1_000_000m, 6);
    }
}

public static class ModelProviders
{
    // Any endpoint that speaks the OpenAI chat API: OpenAI itself, gateways such as LiteLLM or OpenRouter, and local servers such as
    // Ollama, LM Studio or vLLM.
    public const string OpenAi = "openai";

    public const string Anthropic = "anthropic";

    public static IReadOnlyList<string> All { get; } = [OpenAi, Anthropic];
}
