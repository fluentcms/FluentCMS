using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// 'fluentcms keys': a command runs in a process of its own, so here in a host of its own on the database of the instance.
public sealed partial class AuthorizationServerTests
{
    private const string CertificatePassword = "test-password";

    private static readonly TimeSpan DefaultOverlap = TimeSpan.FromDays(14) + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5);

    [Fact]
    public async Task KeysRotateMakesTheNextKeysWithTheLeadAndASecondRunMakesNone()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var before = await host.SignInAsync(editor, "openid", "offline_access", "management");
            var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            var firstEncryption = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
            await using var tool = NewHost().UseSqlite(file);

            var rotated = await tool.RunCommandAsync("keys", "rotate");

            rotated.ExitCode.ShouldBe(0, rotated.Error);
            var keys = SigningKeys(host);
            var second = keys.Single(key => key.Use == SigningKey.Signing && key.Id != first.Id);
            var secondEncryption = keys.Single(key => key.Use == SigningKey.Encryption && key.Id != firstEncryption.Id);
            foreach (var (made, followed) in new[] { (second, first), (secondEncryption, firstEncryption) })
            {
                made.Sequence.ShouldBe(2);
                made.ActiveFrom.ShouldBe(made.CreatedAt.AddDays(1));
                made.RetiredAt.ShouldBeNull();
                keys.Single(key => key.Id == followed.Id).RetiredAt.ShouldBe(made.ActiveFrom + DefaultOverlap);
            }

            rotated.Output.ShouldContain($"Made the signing key {second.Id:D}. It is accepted now and signs from ");
            rotated.Output.ShouldContain($"Made the encryption key {secondEncryption.Id:D}. It is accepted now and encrypts from ");

            var again = await tool.RunCommandAsync("keys", "rotate");
            again.ExitCode.ShouldBe(0, again.Error);
            again.Output.ShouldContain($"The signing key {second.Id:D} is upcoming already");
            again.Output.ShouldContain($"The encryption key {secondEncryption.Id:D} is upcoming already");
            SigningKeys(host).Count.ShouldBe(4);

            // The running instance learns the key within 30 seconds, signs with it after the lead, and signs nobody out.
            using var client = host.CreateSecureClient();
            host.Time.Advance(SigningKeyRing.ReadInterval);
            (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);
            host.Time.Set(second.ActiveFrom);
            var after = await TokenAsync(client, Refresh(before.RefreshToken!), Token);
            Header(after.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));
            Header(after.RefreshToken!).GetProperty("kid").GetString().ShouldBe(KeyId(secondEncryption));
        });
    }

    [Fact]
    public async Task KeysRotateTakesOneUseAndRefusesAnotherWord()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            await using var tool = NewHost().UseSqlite(file);

            var refused = await tool.RunCommandAsync("keys", "rotate", "--use", "jwk");
            refused.ExitCode.ShouldBe(2);
            refused.Error.ShouldStartWith("FCMS3359: --use is sig");
            SigningKeys(host).Count.ShouldBe(2);

            var rotated = await tool.RunCommandAsync("keys", "rotate", "--use", "enc");
            rotated.ExitCode.ShouldBe(0, rotated.Error);
            rotated.Output.ShouldNotContain("signing key");
            SigningKeys(host).Select(key => (key.Use, key.Sequence)).ShouldBe([(SigningKey.Encryption, 1), (SigningKey.Encryption, 2), (SigningKey.Signing, 1)], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task KeysRotateRefusesTheSigningKeyWhileACertificateSigns()
    {
        await InDatabaseFileAsync(async file =>
        {
            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            await using var host = WithCertificate(NewHost().UseSqlite(file), certificate.Path);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            await using var tool = WithCertificate(NewHost().UseSqlite(file), certificate.Path);

            var refused = await tool.RunCommandAsync("keys", "rotate", "--use", "sig");
            refused.ExitCode.ShouldBe(2);
            refused.Error.ShouldStartWith("FCMS3390: FluentCms:Security:SigningCertificate signs the tokens");
            SigningKeys(host).ShouldHaveSingleItem().Use.ShouldBe(SigningKey.Encryption);

            var rotated = await tool.RunCommandAsync("keys", "rotate");
            rotated.ExitCode.ShouldBe(0, rotated.Error);
            rotated.Error.ShouldContain("only the encryption key is rotated");
            SigningKeys(host).Select(key => (key.Use, key.Sequence)).ShouldBe([(SigningKey.Encryption, 1), (SigningKey.Encryption, 2)], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task KeysListShowsEachKeyWithItsStateAndDatesAndNeverTheKey()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            var now = host.Time.GetUtcNow();
            var retiring = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            var encrypts = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
            Retire(host, retiring.Id, now.AddDays(3));
            var retired = AddKey(host, SigningKey.Signing, sequence: 0, now.AddDays(-30), retiredAt: now.AddDays(-1));
            var active = AddKey(host, SigningKey.Signing, sequence: 2, now.AddDays(-1));
            var upcoming = AddKey(host, SigningKey.Signing, sequence: 3, now.AddDays(1));
            await using var tool = NewHost().UseSqlite(file);

            var listed = await tool.RunCommandAsync("keys", "list");

            listed.ExitCode.ShouldBe(0, listed.Error);
            var lines = listed.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            lines[0].ShouldStartWith("ID");
            KeyColumns().Split(lines[0]).ShouldBe(["ID", "USE", "ALGORITHM", "STATE", "CREATED", "ACTIVE FROM", "RETIRES"]);

            Row(lines, retired).ShouldContain(" sig ");
            Row(lines, retired).ShouldContain(" retired ");
            Row(lines, retiring).ShouldContain(" retiring ");
            Row(lines, retiring).TrimEnd().ShouldEndWith(now.AddDays(3).UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            Row(lines, active).ShouldContain(" active ");
            Row(lines, active).ShouldContain(" RS256 ");
            Row(lines, active).TrimEnd().ShouldEndWith(" -");
            Row(lines, upcoming).ShouldContain(" upcoming ");
            Row(lines, upcoming).ShouldContain(now.AddDays(1).UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            Row(lines, encrypts).ShouldContain(" enc ");
            Row(lines, encrypts).ShouldContain(" active ");
            lines.Length.ShouldBe(6);
            foreach (var key in SigningKeys(host))
            {
                listed.Output.ShouldNotContain(key.ProtectedKey);
            }

            // Listing makes no key and changes none.
            SigningKeys(host).Count.ShouldBe(5);
        });
    }

    [Fact]
    public async Task KeysListNamesTheSigningCertificateAndShowsAGeneratedKeyItReplacedAsRetiring()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using (var generated = NewHost().UseSqlite(file))
            {
                await generated.SignInAsync(await generated.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            }

            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            await using var tool = WithCertificate(NewHost().UseSqlite(file), certificate.Path);

            var listed = await tool.RunCommandAsync("keys", "list");

            listed.ExitCode.ShouldBe(0, listed.Error);
            var lines = listed.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Row(lines, SigningKeys(tool).Single(key => key.Use == SigningKey.Signing)).ShouldContain(" retiring ");
            var named = lines.Single(line => line.StartsWith(certificate.Thumbprint, StringComparison.Ordinal));
            named.ShouldContain(" signs ");
            named.ShouldContain(certificate.NotBefore.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            named.ShouldEndWith(certificate.NotAfter.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            lines.ShouldContain(line => line.StartsWith("CERTIFICATE", StringComparison.Ordinal) && line.Contains("NOT AFTER", StringComparison.Ordinal));
        });
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex KeyColumns();

    private static string Row(string[] lines, SigningKey key) => lines.Single(line => line.StartsWith(key.Id.ToString("D"), StringComparison.Ordinal)) + " ";

    private static FluentCmsTestHost WithCertificate(FluentCmsTestHost host, string path) =>
        host.WithServices(services => services.Configure<SecurityOptions>(security =>
        {
            security.SigningCertificate.Path = path;
            security.SigningCertificate.Password = CertificatePassword;
        }));

    // A self-signed certificate with its RSA key in a PKCS#12 file, valid from yesterday for a month unless told otherwise.
    private static (string Path, string Thumbprint, DateTimeOffset NotBefore, DateTimeOffset NotAfter) WriteCertificate(string directory, string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest($"CN=FluentCMS test {name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
        return (path, certificate.Thumbprint, certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime());
    }
}
