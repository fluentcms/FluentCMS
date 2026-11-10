using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Identity;

// Address allowlists and rotation with an overlap (#2936).
public sealed class ApiKeyAddressAndRotationTests : IDisposable
{
    private static readonly TimeSpan Overlap = TimeSpan.FromHours(2);

    private readonly IdentityFixture _fixture = new(cms => cms.ApiKeys.RotationOverlap = Overlap, activity: true);

    public void Dispose() => _fixture.Dispose();

    private Task<T> WithKeysAsync<T>(Func<ApiKeyService, Task<T>> work) =>
        _fixture.WithAsync(services => work(services.GetRequiredService<ApiKeyService>()));

    private Task<CreatedApiKey> CreateAsync(params string[]? addresses) =>
        WithKeysAsync(keys => keys.CreateAsync(new ApiKeyRequest("Reader", ApiKeyClass.Delivery, BuiltInRoles.Editor) { AllowedAddresses = addresses }, Token));

    private Task<CreatedApiKey> RotateAsync(Guid id, TimeSpan? overlap = null) => WithKeysAsync(keys => keys.RotateAsync(id, overlap, Token));

    private Task<AuthenticateResult> AuthenticateAsync(string secret, string? address = null) =>
        _fixture.WithAsync(services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            http.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
            http.Request.Headers.Authorization = $"ApiKey {secret}";
            return http.AuthenticateAsync(ApiKeyAuthentication.Scheme);
        });

    private Task<ApiKeyCheck> CheckAsync(string secret, string? address = null) =>
        WithKeysAsync(keys => keys.AuthenticateAsync(secret, address is null ? null : IPAddress.Parse(address), Token));

    private async Task<List<ActivityRow>> DrainAsync()
    {
        await _fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        return _fixture.ActivityRows();
    }

    private ApiKey Row(Guid id) => _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(key => key.Id == id));

    [Fact]
    public async Task AllowedAddressesAreStoredInTheirPlainFormAndWhatIsNotOneIsRefused()
    {
        var created = await CreateAsync("203.0.113.0/24", " 198.51.100.7 ", "2001:DB8::/32", "2001:db8:0:0::1", "203.0.113.0/24");

        created.Key.AllowedAddresses.ShouldBe(["203.0.113.0/24", "198.51.100.7", "2001:db8::/32", "2001:db8::1"]);
        Row(created.Key.Id).AllowedAddresses.ShouldNotBeNull();
        (await WithKeysAsync(keys => keys.FindAsync(created.Key.Id, Token)))!.AllowedAddresses.ShouldBe(created.Key.AllowedAddresses);

        var refused = await Should.ThrowAsync<ValidationException>(() => CreateAsync("203.0.113.5/24", "10", "10.1.2", "::ffff:10.0.0.1", "fe80::1%2", "10.0.0.0/33", "host.example", ""));
        refused.Errors.Select(error => error.Code).Distinct().ShouldBe([ApiKeyErrorCodes.InvalidAddress]);
        refused.Errors.Select(error => error.Path).ShouldBe([.. Enumerable.Range(0, 8).Select(index => $"allowedAddresses[{index}]")]);

        (await Should.ThrowAsync<ValidationException>(() => CreateAsync([]))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.EmptyBinding);
        var many = Enumerable.Range(0, ApiKeyAddresses.MaxEntries + 1).Select(index => $"10.0.{index / 256}.{index % 256}").ToArray();
        (await Should.ThrowAsync<ValidationException>(() => CreateAsync(many))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.InvalidAddress);
    }

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("::ffff:203.0.113.9")]
    [InlineData("198.51.100.7")]
    [InlineData("2001:db8:5::1")]
    public async Task AKeyWithAnAllowlistIsAcceptedFromAnAllowedAddress(string address)
    {
        var created = await CreateAsync("203.0.113.0/24", "198.51.100.7", "2001:db8::/32");

        (await AuthenticateAsync(created.Secret, address)).Succeeded.ShouldBeTrue(address);
    }

    [Fact]
    public async Task AKeyWithAnAllowlistIsRefusedFromAnotherAddressWithTheCodeOfAWrongKeyAndTheLogSaysWhy()
    {
        var created = await CreateAsync("203.0.113.0/24");

        foreach (var address in new string?[] { "203.0.114.1", "::ffff:198.51.100.1", "2001:db8::1", null })
        {
            var result = await AuthenticateAsync(created.Secret, address);
            result.Succeeded.ShouldBeFalse(address);
            result.Failure!.Message.ShouldStartWith(ApiKeyErrorCodes.InvalidKey);
            (await CheckAsync(created.Secret, address)).Failure.ShouldBe(ApiKeyErrorCodes.AddressRefused, address);
        }

        _fixture.Logs.Messages.ShouldContain(message => message.Contains(ApiKeyErrorCodes.AddressRefused, StringComparison.Ordinal) && message.Contains("203.0.114.1", StringComparison.Ordinal));
        _fixture.Logs.Messages.ShouldNotContain(message => message.Contains(created.Secret, StringComparison.Ordinal));

        // A key with no allowlist is accepted from any address, and from none.
        var open = await CreateAsync(null);
        (await AuthenticateAsync(open.Secret, "192.0.2.1")).Succeeded.ShouldBeTrue();
        (await AuthenticateAsync(open.Secret)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ARefusedAddressIsAnActivityRowOncePerIntervalAndAddress()
    {
        var created = await CreateAsync("203.0.113.0/24");

        await AuthenticateAsync(created.Secret, "192.0.2.1");
        await AuthenticateAsync(created.Secret, "::ffff:192.0.2.1");
        await AuthenticateAsync(created.Secret, "192.0.2.2");
        _fixture.Clock.Advance(ApiKeyRefusals.Interval);
        await AuthenticateAsync(created.Secret, "192.0.2.1");

        // A wrong key from anywhere writes none.
        await AuthenticateAsync(created.Secret[..16] + new string('A', created.Secret.Length - 16), "192.0.2.9");

        var rows = (await DrainAsync()).Where(row => row.Action == "api_key.address_refused").ToList();
        rows.Select(row => row.Ip).ShouldBe(["192.0.2.1", "192.0.2.2", "192.0.2.1"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.TargetType == "api_key" && row.TargetId == created.Key.Id.ToString("D") && row.SiteId == null);
        rows.ShouldAllBe(row => row.ActorType == PrincipalKind.Anonymous && row.Details!.Contains(ApiKeyErrorCodes.AddressRefused));
    }

    [Fact]
    public async Task AKeySentFromManyAddressesWritesABoundedNumberOfRowsPerInterval()
    {
        var created = await CreateAsync("203.0.113.0/24");

        for (var index = 1; index <= ApiKeyRefusals.MaxPerKey + 5; index++)
        {
            await AuthenticateAsync(created.Secret, $"2001:db8:1::{index:x}");
        }

        (await DrainAsync()).Count(row => row.Action == "api_key.address_refused").ShouldBe(ApiKeyRefusals.MaxPerKey);
    }

    [Fact]
    public async Task ARevokedKeyHasNoAllowlistChangeAndShowsNoOverlap()
    {
        var created = await CreateAsync(null);
        await RotateAsync(created.Key.Id);
        (await WithKeysAsync(keys => keys.RevokeAsync(created.Key.Id, Token))).ShouldBeTrue();

        (await WithKeysAsync(keys => keys.FindAsync(created.Key.Id, Token)))!.PreviousKeyExpiresAt.ShouldBeNull();
        (await Should.ThrowAsync<ValidationException>(() => WithKeysAsync(keys => keys.SetAllowedAddressesAsync(created.Key.Id, ["192.0.2.1"], Token))))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.NotRotatable);
    }

    [Fact]
    public async Task TheAllowlistOfAKeyIsChangedAndCleared()
    {
        var created = await CreateAsync("203.0.113.0/24");

        var changed = await WithKeysAsync(keys => keys.SetAllowedAddressesAsync(created.Key.Id, ["192.0.2.0/24"], Token));
        changed.AllowedAddresses.ShouldBe(["192.0.2.0/24"]);
        (await CheckAsync(created.Secret, "203.0.113.1")).Failure.ShouldBe(ApiKeyErrorCodes.AddressRefused);
        (await CheckAsync(created.Secret, "192.0.2.1")).Failure.ShouldBeNull();

        (await WithKeysAsync(keys => keys.SetAllowedAddressesAsync(created.Key.Id, null, Token))).AllowedAddresses.ShouldBeNull();
        (await CheckAsync(created.Secret, "203.0.113.1")).Failure.ShouldBeNull();

        (await Should.ThrowAsync<ValidationException>(() => WithKeysAsync(keys => keys.SetAllowedAddressesAsync(created.Key.Id, [], Token))))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.EmptyBinding);
        (await Should.ThrowAsync<ValidationException>(() => WithKeysAsync(keys => keys.SetAllowedAddressesAsync(Guid.NewGuid(), null, Token))))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.NotFound);

        var rows = (await DrainAsync()).Where(row => row.Action == "api_key.allowed_addresses_changed").ToList();
        rows.Count.ShouldBe(2);
        rows.ShouldContain(row => row.Details != null && row.Details.Contains("192.0.2.0/24"));
    }

    [Fact]
    public async Task ARotatedKeyAcceptsBothSecretsUntilTheOverlapEndsAndOnlyTheNewOneAfter()
    {
        var created = await CreateAsync(null);

        var rotated = await RotateAsync(created.Key.Id);
        rotated.Key.Id.ShouldBe(created.Key.Id);
        rotated.Secret.ShouldStartWith("fcms_dk_");
        rotated.Secret.ShouldNotBe(created.Secret);
        rotated.Key.KeyPrefix.ShouldBe(rotated.Secret[..ApiKeyFormat.LookupLength]);
        rotated.Key.PreviousKeyExpiresAt.ShouldBe(IdentityFixture.Start + Overlap);

        (await AuthenticateAsync(created.Secret)).Succeeded.ShouldBeTrue();
        (await AuthenticateAsync(rotated.Secret)).Succeeded.ShouldBeTrue();

        _fixture.Clock.Advance(Overlap - TimeSpan.FromSeconds(1));
        (await AuthenticateAsync(created.Secret)).Succeeded.ShouldBeTrue();

        _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await CheckAsync(created.Secret)).Failure.ShouldBe(ApiKeyErrorCodes.InvalidKey);
        (await AuthenticateAsync(rotated.Secret)).Succeeded.ShouldBeTrue();
        (await WithKeysAsync(keys => keys.FindAsync(created.Key.Id, Token)))!.PreviousKeyExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task ASecondRotationWithinTheOverlapLeavesAtMostTwoSecrets()
    {
        var created = await CreateAsync(null);
        var first = await RotateAsync(created.Key.Id);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        var second = await RotateAsync(created.Key.Id);

        (await CheckAsync(created.Secret)).Failure.ShouldBe(ApiKeyErrorCodes.InvalidKey);
        (await CheckAsync(first.Secret)).Failure.ShouldBeNull();
        (await CheckAsync(second.Secret)).Failure.ShouldBeNull();
        second.Key.PreviousKeyExpiresAt.ShouldBe(IdentityFixture.Start + TimeSpan.FromMinutes(10) + Overlap);
    }

    [Fact]
    public async Task ARotationMayEndTheOldSecretSoonerButNotLater()
    {
        var created = await CreateAsync(null);

        var now = await RotateAsync(created.Key.Id, TimeSpan.Zero);
        now.Key.PreviousKeyExpiresAt.ShouldBeNull();
        (await CheckAsync(created.Secret)).Failure.ShouldBe(ApiKeyErrorCodes.InvalidKey);
        (await CheckAsync(now.Secret)).Failure.ShouldBeNull();

        var shorter = await RotateAsync(created.Key.Id, TimeSpan.FromMinutes(5));
        shorter.Key.PreviousKeyExpiresAt.ShouldBe(IdentityFixture.Start + TimeSpan.FromMinutes(5));

        foreach (var overlap in new[] { Overlap + TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1) })
        {
            (await Should.ThrowAsync<ValidationException>(() => RotateAsync(created.Key.Id, overlap))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.InvalidOverlap);
        }
    }

    [Fact]
    public async Task ARevokedOrExpiredKeyIsNotRotatedAndRevokingARotatedKeyRefusesBothSecrets()
    {
        var created = await CreateAsync(null);
        var rotated = await RotateAsync(created.Key.Id);

        (await WithKeysAsync(keys => keys.RevokeAsync(created.Key.Id, Token))).ShouldBeTrue();
        (await CheckAsync(created.Secret)).Failure.ShouldBe(ApiKeyErrorCodes.Revoked);
        (await CheckAsync(rotated.Secret)).Failure.ShouldBe(ApiKeyErrorCodes.Revoked);
        (await Should.ThrowAsync<ValidationException>(() => RotateAsync(created.Key.Id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.NotRotatable);

        var expiring = await WithKeysAsync(keys => keys.CreateAsync(new ApiKeyRequest("Short", ApiKeyClass.Delivery, BuiltInRoles.Editor) { ExpiresAt = IdentityFixture.Start.AddMinutes(1) }, Token));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        (await Should.ThrowAsync<ValidationException>(() => RotateAsync(expiring.Key.Id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.NotRotatable);
    }

    [Fact]
    public async Task TheOldSecretFromAWrongAddressIsRefusedForTheAddress()
    {
        var created = await CreateAsync("203.0.113.0/24");
        await RotateAsync(created.Key.Id);

        (await CheckAsync(created.Secret, "203.0.113.1")).Failure.ShouldBeNull();
        (await CheckAsync(created.Secret, "192.0.2.1")).Failure.ShouldBe(ApiKeyErrorCodes.AddressRefused);
    }

    [Fact]
    public async Task ARotationIsAnEventAndAnActivityRowWithoutEitherSecretOrItsHash()
    {
        var created = await CreateAsync(null);
        var oldHash = Row(created.Key.Id).KeyHash;
        var rotated = await RotateAsync(created.Key.Id);
        var newHash = Row(created.Key.Id).KeyHash;

        var outbox = _fixture.Read(context => context.Set<OutboxMessage>().AsNoTracking().Select(message => message.EventType + message.Payload).ToList());
        var rows = await DrainAsync();

        var row = rows.Single(item => item.Action == "api_key.rotated");
        row.TargetId.ShouldBe(created.Key.Id.ToString("D"));
        row.Details.ShouldNotBeNull().ShouldContain("2027-03-01T10:00:00");

        List<string> written = [.. outbox, .. rows.Select(item => $"{item.TargetLabel} {item.Details}"), .. _fixture.Logs.Messages];
        foreach (var secret in new[] { created.Secret[8..16], rotated.Secret[8..16] })
        {
            written.ShouldNotContain(text => text.Contains(secret, StringComparison.Ordinal));
        }

        written.ShouldNotContain(text => text.Contains(oldHash, StringComparison.OrdinalIgnoreCase) || text.Contains(newHash, StringComparison.OrdinalIgnoreCase));
    }
}
