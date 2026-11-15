using System.Net;
using System.Net.Sockets;

namespace FluentCMS.Plugins;

/// <summary>
/// Tells a public address from one of the host's own network, for a plugin that connects to a URL someone else names,
/// such as the target of a webhook or the source of an upload. A caller checks every address a host name resolves to and
/// connects to an address it checked, so that DNS cannot point it elsewhere between the two.
/// </summary>
public static class PublicAddresses
{
    private static readonly byte[] Nat64Prefix = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    // 64:ff9b:1::/48, the prefix of RFC 8215 for translators of a network's own, whose IPv4 address may sit anywhere.
    private static readonly byte[] LocalNat64Prefix = [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01];

    // ::ffff:0:0:0/96, the IPv4-translated form of SIIT (RFC 2765).
    private static readonly byte[] TranslatedPrefix = [0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0];

    /// <summary>Checks an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>
    /// <see langword="false"/> for a loopback, private, link-local, carrier-grade NAT, benchmark, multicast, reserved or
    /// unspecified address, in IPv4 and IPv6, for an IPv6 address that carries one of them (IPv4-mapped and
    /// IPv4-compatible forms, NAT64, 6to4) or tunnels to one (Teredo), and for the prefixes of a translator of the network's
    /// own (the local-use NAT64 prefix of RFC 8215 and the IPv4-translated form of SIIT), whatever address they carry.
    /// </returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var length))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork && length == 4)
        {
            return !(bytes[0] is 0 or 10 or 127 or >= 224
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                || (bytes[0] == 198 && (bytes[1] & 0xFE) == 18));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || length != 16)
        {
            return false;
        }

        // The unspecified address, loopback and the IPv4-compatible forms, then NAT64 and 6to4, which carry an IPv4 address,
        // and Teredo, which tunnels to one. A translator of the network's own reaches whatever that network has.
        if (bytes[..12].IndexOfAnyExcept((byte)0) < 0 || bytes.StartsWith(LocalNat64Prefix) || bytes[..12].SequenceEqual(TranslatedPrefix))
        {
            return false;
        }

        if (bytes[..12].SequenceEqual(Nat64Prefix))
        {
            return IsPublic(new IPAddress(bytes[12..16]));
        }

        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return IsPublic(new IPAddress(bytes[2..6]));
        }

        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0)
        {
            return false;
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
    }
}
