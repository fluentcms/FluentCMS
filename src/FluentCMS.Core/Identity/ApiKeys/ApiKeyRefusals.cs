using System.Collections.Concurrent;

namespace FluentCMS.Core.Identity.ApiKeys;

// Which refused addresses of a key were written to the activity log lately, on this instance. A client that keeps sending a
// valid key from the wrong address writes a row once per interval and address, and a key writes at most MaxPerKey rows per
// interval however many addresses it is sent from, since an IPv6 sender has addresses to spare.
internal sealed class ApiKeyRefusals(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public const int MaxPerKey = 10;

    private const int MaxTracked = 4096;

    private readonly Dictionary<Guid, Window> _keys = [];
    private readonly Lock _lock = new();

    public bool ShouldRecord(Guid keyId, string address)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            if (!_keys.TryGetValue(keyId, out var window) || window.Start <= now - Interval)
            {
                if (_keys.Count >= MaxTracked)
                {
                    foreach (var stale in _keys.Where(item => item.Value.Start <= now - Interval).Select(item => item.Key).ToList())
                    {
                        _keys.Remove(stale);
                    }
                }

                window = new Window(now);
                _keys[keyId] = window;
            }

            return window.Addresses.Count < MaxPerKey && window.Addresses.Add(address);
        }
    }

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;

        public HashSet<string> Addresses { get; } = new(StringComparer.Ordinal);
    }
}
