using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentCMS.Content;

namespace FluentCMS.Core.Identity.ApiKeys;

// The addresses a key is accepted from: single addresses and CIDR ranges, kept in the form IPAddress and IPNetwork write.
// A range with bits set past its prefix, an IPv4-mapped IPv6 entry and a zone are refused rather than guessed at: the first
// may be a typo for a wider or a narrower range, and the others are written as the plain address.
internal static class ApiKeyAddresses
{
    public const int MaxEntries = 64;

    public static List<string>? Normalize(IReadOnlyCollection<string>? entries, List<ValidationError> errors)
    {
        if (entries is null)
        {
            return null;
        }

        if (entries.Count == 0)
        {
            errors.Add(new ValidationError("allowedAddresses", ApiKeyErrorCodes.EmptyBinding, "A key is allowed from at least one address, or from any by leaving allowedAddresses out."));
            return null;
        }

        if (entries.Count > MaxEntries)
        {
            errors.Add(new ValidationError("allowedAddresses", ApiKeyErrorCodes.InvalidAddress, $"A key is allowed from at most {MaxEntries} addresses and ranges."));
            return null;
        }

        List<string> normalized = [];
        var index = 0;
        foreach (var entry in entries)
        {
            if (Parse((entry ?? "").Trim()) is { } network)
            {
                normalized.Add(Format(network));
            }
            else
            {
                errors.Add(new ValidationError(
                    $"allowedAddresses[{index}]",
                    ApiKeyErrorCodes.InvalidAddress,
                    $"'{entry}' is not an IP address or a CIDR range such as 203.0.113.0/24 or 2001:db8::/32 with no bits set past its prefix."));
            }

            index++;
        }

        return [.. normalized.Distinct(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string>? Of(ApiKey key) =>
        key.AllowedAddresses is null ? null : JsonSerializer.Deserialize<List<string>>(key.AllowedAddresses) ?? [];

    // An unknown client address is refused, and an IPv4 client that reaches a dual-stack socket is matched as IPv4.
    public static bool Allows(ApiKey key, IPAddress? client)
    {
        if (Of(key) is not { } entries)
        {
            return true;
        }

        if (client is null)
        {
            return false;
        }

        var address = client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;
        return entries.Any(entry => Parse(entry) is { } network && network.Contains(address));
    }

    // IPv4 is taken in its dotted form of four numbers only, since IPAddress also reads "10" or "10.1" as addresses.
    private static IPNetwork? Parse(string entry)
    {
        var slash = entry.IndexOf('/', StringComparison.Ordinal);
        var text = slash < 0 ? entry : entry[..slash];
        if (text.Contains('%', StringComparison.Ordinal)
            || !IPAddress.TryParse(text, out var address)
            || address.IsIPv4MappedToIPv6
            || (address.AddressFamily == AddressFamily.InterNetwork && address.ToString() != text))
        {
            return null;
        }

        if (slash < 0)
        {
            return new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        }

        return IPNetwork.TryParse(entry, out var network) && network.BaseAddress.Equals(address) ? network : null;
    }

    private static string Format(IPNetwork network) =>
        network.PrefixLength == (network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
            ? network.BaseAddress.ToString()
            : network.ToString();
}
