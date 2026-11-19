using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FluentCMS.Sites;

namespace FluentCMS.Plugins.AiAssist;

// The tokens the budgeted calls of each site that are in flight on this instance may still use, which have no usage row
// yet. A call reserves its estimate before any work is done for it and lets it go once its row is written or it is
// refused, so that calls sent together are counted against the budget as one after the other.
internal sealed class AiBudgets
{
    // What a call that sends an image is reckoned to use for it, which is about what OpenAI counts for one of 1024 pixels.
    public const int ImageTokens = 1000;

    // What the instructions of an action are reckoned to use before they are written.
    public const int InstructionTokens = 500;

    private readonly ConcurrentDictionary<Guid, StrongBox<long>> _reserved = new();
    private readonly ConcurrentDictionary<Guid, DateOnly> _reported = new();

    // About four characters a token, rounded up.
    public static long Tokens(long characters) => (characters + 3) / 4;

    public AiReservation Reserve(SiteId site, long tokens)
    {
        var held = _reserved.GetOrAdd(site.Value, static _ => new StrongBox<long>());
        return new AiReservation(held, tokens, Interlocked.Add(ref held.Value, tokens) - tokens);
    }

    // True for the first refusal of a site over its budget on a day on this instance, so that the log names it once a day.
    public bool ReportOverBudget(SiteId site, DateOnly day)
    {
        var reported = false;
        _reported.AddOrUpdate(site.Value, _ => { reported = true; return day; }, (_, last) => { reported = last != day; return day; });
        return reported;
    }
}

// Before is what the site's other calls in flight held when this one reserved.
internal sealed class AiReservation(StrongBox<long> held, long tokens, long before)
{
    private long _tokens = tokens;
    private int _released;

    public long Before => before;

    // To the estimate of the call once what it sends is known.
    public void Change(long tokens)
    {
        if (Volatile.Read(ref _released) == 0)
        {
            Interlocked.Add(ref held.Value, tokens - Interlocked.Exchange(ref _tokens, tokens));
        }
    }

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            Interlocked.Add(ref held.Value, -Interlocked.Read(ref _tokens));
        }
    }
}
