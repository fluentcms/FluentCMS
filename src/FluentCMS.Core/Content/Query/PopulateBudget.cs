namespace FluentCMS.Core.Content.Query;

// The related entries one request has inlined so far. A read of every locale, or one that falls back, runs the engine once
// per locale, and MaxPopulatedEntries holds for the whole answer, not for each of them (#3284).
internal sealed class PopulateBudget
{
    public int Populated { get; set; }
}
