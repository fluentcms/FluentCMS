using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Agents;

namespace FluentCMS.Tests.Agents;

public sealed class AgentKeysTests
{
    [Fact]
    public void A_key_is_the_prefix_and_32_random_bytes_in_base64url()
    {
        var key = AgentKeys.Generate();

        Assert.StartsWith(AgentKeys.Prefix, key, StringComparison.Ordinal);
        Assert.Equal(48, key.Length);
        Assert.Equal(AgentKeys.Length, key.Length);
        Assert.Matches("^fcms_[A-Za-z0-9_-]{43}$", key);
        Assert.True(AgentKeys.IsWellFormed(key));
    }

    [Fact]
    public void Every_key_is_different()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => AgentKeys.Generate()).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_whole_key()
    {
        var key = AgentKeys.Generate();

        var hash = AgentKeys.Hash(key);

        Assert.Equal(32, hash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(key)), hash);
        Assert.Equal(hash, AgentKeys.Hash(key));
        Assert.NotEqual(hash, AgentKeys.Hash(AgentKeys.Generate()));
    }

    [Fact]
    public void Anything_generate_cannot_produce_is_not_well_formed()
    {
        var key = AgentKeys.Generate();
        string?[] malformed =
        [
            null,
            "",
            "   ",
            AgentKeys.Prefix,
            key[..^1],
            key + "A",
            "xxxx_" + key[AgentKeys.Prefix.Length..],
            key.ToUpperInvariant(),
            key[..^1] + "+",
            key[..^1] + "/",
            key[..^1] + " ",
            "Bearer " + key,
        ];

        Assert.All(malformed, candidate => Assert.False(AgentKeys.IsWellFormed(candidate)));
    }
}
