using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using FluentCMS.Sites;

namespace FluentCMS.Plugins.AiAssist;

// The tokens the budgeted calls of each site that are in flight on this instance may still use, which have no usage row
// yet, and those of calls whose row could not be written. A call reserves its estimate before any work is done for it and
// lets it go once its row is written or it is refused, so that calls sent together are counted against the budget as
// one after the other.
internal sealed class AiBudgets
{
    // What a call that sends an image is reckoned to use for it: the copy fits 1024 by 1024 pixels, which OpenAI counts as
    // 765 tokens at the most and Anthropic's formula as about 1400.
    public const int ImageTokens = 1600;

    // The most UTF-8 bytes a character of .NET's strings takes: three for a character of the Basic Multilingual Plane, and
    // four for the pair of two that is any other.
    public const int MaxBytesPerChar = 3;

    private readonly ConcurrentDictionary<Guid, StrongBox<long>> _reserved = new();
    private readonly ConcurrentDictionary<Guid, (DateOnly Day, long Tokens)> _unrecorded = new();
    private readonly ConcurrentDictionary<Guid, DateOnly> _reported = new();

    // One token for each UTF-8 byte, which no tokenizer that works on bytes passes, whatever the script.
    public static long Tokens(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);

    // Before also holds the tokens of the day whose rows could not be written.
    public AiReservation Reserve(SiteId site, DateOnly day, long tokens)
    {
        var held = _reserved.GetOrAdd(site.Value, static _ => new StrongBox<long>());
        var before = Interlocked.Add(ref held.Value, tokens) - tokens;
        var unrecorded = _unrecorded.TryGetValue(site.Value, out var lost) && lost.Day == day ? lost.Tokens : 0;
        return new AiReservation(held, tokens, before + unrecorded);
    }

    // A call that was sent and whose row could not be written still counts for the rest of its day on this instance.
    public void Unrecorded(SiteId site, DateOnly day, long tokens) =>
        _unrecorded.AddOrUpdate(site.Value, (day, tokens), (_, lost) => lost.Day == day ? (day, lost.Tokens + tokens) : (day, tokens));

    // True for the first refusal of a site over its budget on a day on this instance, so that the log names it once a day.
    public bool ReportOverBudget(SiteId site, DateOnly day)
    {
        var reported = false;
        _reported.AddOrUpdate(site.Value, _ => { reported = true; return day; }, (_, last) => { reported = last != day; return day; });
        return reported;
    }
}

// Before is what the site's other calls in flight and its unwritten rows held when this one reserved.
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
