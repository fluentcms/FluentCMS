using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// /api/members/v1: registering, verifying, resetting and /me, against a real host on SQLite.
public sealed class MemberAccountTests(MemberFixture fixture) : IClassFixture<MemberFixture>
{
    private const string Password = MemberFixture.Password;

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AVisitorRegistersGetsAVerificationMailVerifiesAndReadsItsProfile()
    {
        var address = MemberFixture.NewAddress();

        var registered = await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}", "displayName": "Mia" }""");
        registered.Status.ShouldBe(HttpStatusCode.Accepted);
        registered.Body.ShouldBeNull();

        // The link goes to the hosted page on the public origin, with the address and the token.
        var link = fixture.Emails.LinkTo(address);
        link.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/members/verify-email");
        link.Query.ShouldContain("email=" + Uri.EscapeDataString(address));

        // Until the address is verified the member gets no token.
        (await fixture.DirectLoginAsync(address, Password)).Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        (await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);

        var tokens = await fixture.DirectLoginAsync(address, Password);
        tokens.Status.ShouldBe(HttpStatusCode.OK, tokens.Body?.ToJsonString());
        using var member = fixture.Bearer(tokens.Json["access_token"]!.GetValue<string>());
        var me = await fixture.MembersAsync(HttpMethod.Get, "me", client: member);
        me.Status.ShouldBe(HttpStatusCode.OK, me.Body?.ToJsonString());
        me.Json["email"]!.GetValue<string>().ShouldBe(address);
        me.Json["emailVerified"]!.GetValue<bool>().ShouldBeTrue();
        me.Json["displayName"]!.GetValue<string>().ShouldBe("Mia");
        me.Json["groups"]!.AsArray().ShouldBeEmpty();
        me.Headers.CacheControl!.NoStore.ShouldBeTrue();

        // The member is the actor of what it did to its own account, and the rows belong to no site.
        var id = (await fixture.MemberIdAsync(address)).ToString("D");
        var rows = await fixture.ActivityAsync("member.", id);
        var created = rows.Single(row => row.Action == "member.registered");
        created.ActorType.ShouldBe(PrincipalKind.Member);
        created.ActorId.ToString("D").ShouldBe(id);
        created.SiteId.ShouldBeNull();
        created.TargetType.ShouldBe("member");
        created.TargetLabel.ShouldBe(address);
        rows.Single(row => row.Action == "member.verified").ActorType.ShouldBe(PrincipalKind.Member);
        var signedIn = rows.Single(row => row.Action == "member.login_succeeded");
        signedIn.ActorType.ShouldBe(PrincipalKind.Member);
        JsonSerializer.Deserialize<Dictionary<string, string?>>(signedIn.Details!)!["method"].ShouldBe(MemberLoginMethods.DirectLogin);
        rows.Single(row => row.Action == "member.login_failed").ActorType.ShouldBe(PrincipalKind.Anonymous);
    }

    [Fact]
    public async Task RegisterResendAndForgotAnswerTheSameForAKnownAndAnUnknownAddress()
    {
        var verified = await fixture.RegisterVerifiedAsync();
        var pending = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var unknown = MemberFixture.NewAddress();
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));

        foreach (var route in (string[])["resend-verification", "forgot-password"])
        {
            foreach (var address in (string[])[verified, pending, unknown])
            {
                var reply = await fixture.MembersAsync(HttpMethod.Post, route, $$"""{ "email": "{{address}}" }""");
                reply.Status.ShouldBe(HttpStatusCode.Accepted, $"{route} for {address}");
                reply.Body.ShouldBeNull();
            }
        }

        // An address that is no member's gets no mail from either route.
        fixture.MailsTo(unknown).ShouldBe(0);

        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        foreach (var address in (string[])[verified, pending, unknown])
        {
            var reply = await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""");
            reply.Status.ShouldBe(HttpStatusCode.Accepted, $"register for {address}");
            reply.Body.ShouldBeNull();
        }

        // A registered address is told by mail and not in the answer, and its account is as it was.
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Last(mail => mail.To == verified).Subject.ShouldBe("You already have an account");
            fixture.Emails.Sent.Last(mail => mail.To == unknown).Subject.ShouldBe("Verify your email address");
        }

        (await fixture.DirectLoginAsync(verified, Password)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResendAndForgotDoTheSameWorkForAKnownAndAnUnknownAddressAndLeaveTheMailToAJob()
    {
        var verified = await fixture.RegisterVerifiedAsync();
        var pending = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var unknown = MemberFixture.NewAddress();
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        var sent = fixture.Emails.Count;

        // Each request enqueues one job and sends nothing, whoever the address is.
        using var client = fixture.Host.CreateSecureClient();
        foreach (var route in (string[])["resend-verification", "forgot-password"])
        {
            foreach (var address in (string[])[verified, pending, unknown])
            {
                var before = await AccountMailJobs.PendingAsync(fixture.Host, MemberMailJob.Type);
                (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + route, $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
                (await AccountMailJobs.PendingAsync(fixture.Host, MemberMailJob.Type)).ShouldBe(before + 1, $"{route} for {address}");
            }
        }

        fixture.Emails.Count.ShouldBe(sent);

        // The jobs send to the members, the verification mail only to the one that is not verified, and nothing to the
        // address that is no member's.
        (await AccountMailJobs.RunAsync(fixture.Host)).ShouldBe(6);
        fixture.MailsTo(unknown).ShouldBe(0);
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Skip(sent).Select(mail => (mail.To, mail.Subject)).OrderBy(mail => mail.To).ThenBy(mail => mail.Subject).ShouldBe(
                new[] { (pending, "Verify your email address"), (verified, "Reset your password") }.OrderBy(mail => mail.Item1).ThenBy(mail => mail.Item2));
        }
    }

    [Fact]
    public async Task TheAnswerDoesNotWaitForTheMailToGo()
    {
        var emails = new BlockingEmailSender();
        await using var host = MemberFixture.NewHost(emails, configure: cms => cms.Jobs.Options.Queues[Core.Identity.AccountMail.Queue] = 1);
        var member = await host.CreateMemberAsync(password: Password, cancellationToken: Token);
        using var client = host.CreateSecureClient();

        // The worker of the mail queue sends while the answer is already back; were the answer waiting, it never came.
        var reply = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "forgot-password", $$"""{ "email": "{{member.Email}}" }""").WaitAsync(TimeSpan.FromSeconds(30), Token);
        reply.Status.ShouldBe(HttpStatusCode.Accepted);
        (await emails.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token)).To.ShouldBe(member.Email);
        emails.Sent.ShouldBe(0);

        emails.Release();
    }

    // On a host of its own, since it moves the clock by a week, past the tokens of the shared host's admin.
    [Fact]
    public async Task AVerificationLinkSentNearTheEndExpiresWhenTheMemberMayBeRemoved()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        var logs = new LogCapture();
        await using var host = MemberFixture.NewHost(emails, logs);
        using var client = host.CreateSecureClient();
        async Task<Management.ApiResponse> CallAsync(string route, string json)
        {
            var response = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + route, json);
            await AccountMailJobs.RunAsync(host);
            return response;
        }

        int MailsTo(string to)
        {
            lock (emails.Sent)
            {
                return emails.Sent.Count(mail => mail.To == to);
            }
        }

        string LastTo(string to)
        {
            lock (emails.Sent)
            {
                return emails.Sent.Last(mail => mail.To == to).TextBody;
            }
        }

        string TokenOf(string to) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(emails.LinkTo(to).Query)["token"].ToString();

        var address = MemberFixture.NewAddress();
        (await CallAsync("register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        DateTimeOffset createdAt;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var normalized = address.ToUpperInvariant();
            createdAt = (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().AsNoTracking()
                .SingleAsync(member => member.NormalizedEmail == normalized, Token)).CreatedAt;
        }

        var removable = createdAt + new MemberOptions().UnverifiedMemberLifetime;
        var expiry = $"expires at {removable.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UTC";

        // Two hours before the job may remove the member, the link of a mail sent again is good for those two hours.
        host.Time.Set(removable - TimeSpan.FromHours(2));
        (await CallAsync("resend-verification", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        MailsTo(address).ShouldBe(2);
        LastTo(address).ShouldContain(expiry);
        var token = TokenOf(address);

        // A reset link of the member, good for an hour, ends at the same time when less is left.
        host.Time.Set(removable - TimeSpan.FromMinutes(30));
        (await CallAsync("forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        MailsTo(address).ShouldBe(3);
        emails.LinkTo(address).AbsolutePath.ShouldBe("/account/members/reset-password");
        LastTo(address).ShouldContain(expiry);
        var resetToken = TokenOf(address);

        host.Time.Set(removable);
        var late = await CallAsync("verify-email", $$"""{ "email": "{{address}}", "token": "{{token}}" }""");
        late.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        late.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");
        var lateReset = await CallAsync("reset-password", $$"""{ "email": "{{address}}", "token": "{{resetToken}}", "password": "{{MemberFixture.NewPassword}}" }""");
        lateReset.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        lateReset.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");

        // From then on a mail would carry a link that has expired already, so none goes.
        (await CallAsync("resend-verification", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        MailsTo(address).ShouldBe(3);
        logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.MailTooLateId);
    }

    [Fact]
    public async Task AnAddressGetsNoMoreThanOneMailItAskedForAndOneNoticeAMinute()
    {
        var address = MemberFixture.NewAddress();
        var body = $$"""{ "email": "{{address}}" }""";

        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(1);

        // Within the minute another registration answers as before and sends nothing.
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address.ToUpperInvariant()}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(1);
        fixture.Logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.MailHeldBackId);

        // What the owner asks for has a slot of its own, which the first of the two routes takes for the minute.
        (await fixture.MembersAsync(HttpMethod.Post, "resend-verification", body)).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(2);
        (await fixture.MembersAsync(HttpMethod.Post, "forgot-password", body)).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(2);

        fixture.Host.Time.Advance(MemberMailLimiter.Window - TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "resend-verification", body)).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(2);

        fixture.Host.Time.Advance(TimeSpan.FromSeconds(2));
        (await fixture.MembersAsync(HttpMethod.Post, "resend-verification", body)).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(3);
    }

    [Fact]
    public async Task ANoticeAVisitorCausesDoesNotHoldBackTheResetTheOwnerAsksFor()
    {
        var address = await fixture.RegisterVerifiedAsync();
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));

        // Someone registers the owner's address, and the owner gets the note that it has an account.
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Last(mail => mail.To == address).Subject.ShouldBe("You already have an account");
        }

        // Within the same minute the owner asks for a reset, and gets it.
        (await fixture.MembersAsync(HttpMethod.Post, "forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Last(mail => mail.To == address).Subject.ShouldBe("Reset your password");
        }

        var reset = await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}", "password": "{{MemberFixture.NewPassword}}" }""");
        reset.Status.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AVerificationTokenWorksOnceAndAWrongOneIsAnsweredLikeAnUnknownAddress()
    {
        var address = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var token = fixture.TokenOf(address);
        var verify = $$"""{ "email": "{{address}}", "token": "{{token}}" }""";

        var wrong = await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "not-a-token" }""");
        var stranger = await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{MemberFixture.NewAddress()}}", "token": "{{token}}" }""");
        wrong.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        wrong.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");
        stranger.Status.ShouldBe(wrong.Status);
        stranger.Json["detail"]!.GetValue<string>().ShouldBe(wrong.Json["detail"]!.GetValue<string>());

        (await fixture.MembersAsync(HttpMethod.Post, "verify-email", verify)).Status.ShouldBe(HttpStatusCode.NoContent);

        var again = await fixture.MembersAsync(HttpMethod.Post, "verify-email", verify);
        again.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        again.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");
    }

    [Fact]
    public async Task AResetTokenWorksOnceAndAResetEndsTheMembersSessions()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var direct = await fixture.DirectLoginAsync(address, Password);
        direct.Status.ShouldBe(HttpStatusCode.OK, direct.Body?.ToJsonString());
        var hosted = await fixture.Host.SignInAsync(new TestMember(await fixture.MemberIdAsync(address), address, Principal.Anonymous));
        using var before = fixture.Bearer(direct.Json["access_token"]!.GetValue<string>());
        using var beforeHosted = fixture.Bearer(hosted.AccessToken);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: before)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: beforeHosted)).Status.ShouldBe(HttpStatusCode.OK);

        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.Emails.LinkTo(address).GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/members/reset-password");
        var reset = $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}", "password": "{{MemberFixture.NewPassword}}" }""";

        // A password the policy refuses leaves the token as it is.
        var weak = await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}", "password": "short" }""");
        weak.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        weak.Json["code"]!.GetValue<string>().ShouldBe("FCMS3165");

        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", reset)).Status.ShouldBe(HttpStatusCode.NoContent);

        // The access tokens stop at once, the refresh tokens are refused, and the old password is gone.
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: before)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: beforeHosted)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(direct.Json["refresh_token"]!.GetValue<string>(), MemberFixture.DirectClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.RefreshAsync(hosted.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, MemberFixture.NewPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var again = await fixture.MembersAsync(HttpMethod.Post, "reset-password", reset);
        again.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        again.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");

        var updated = (await fixture.ActivityAsync("member.updated", (await fixture.MemberIdAsync(address)).ToString("D"))).ShouldHaveSingleItem();
        updated.Details!.ShouldContain(MemberChanges.Password);
        updated.Details!.ShouldNotContain(MemberFixture.NewPassword);
    }

    // On a host of its own, since it moves the clock by a day, past the tokens of the shared host's admin. Of two links made
    // at once, one is used at the end of its lifetime and the other a tick later.
    [Fact]
    public async Task AVerificationLinkWorksForTwentyFourHoursAndAResetLinkForAnHourOnTheHostsClock()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails);
        using var client = host.CreateSecureClient();
        async Task<Management.ApiResponse> CallAsync(string route, string json)
        {
            var response = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + route, json);
            await AccountMailJobs.RunAsync(host);
            return response;
        }

        Task<Management.ApiResponse> VerifyAsync(string address) =>
            CallAsync("verify-email", $$"""{ "email": "{{address}}", "token": "{{TokenOf(emails, address)}}" }""");

        Task<Management.ApiResponse> ResetAsync(string address) =>
            CallAsync("reset-password", $$"""{ "email": "{{address}}", "token": "{{TokenOf(emails, address)}}", "password": "{{MemberFixture.NewPassword}}" }""");

        var (verified, unverified) = (MemberFixture.NewAddress(), MemberFixture.NewAddress());
        foreach (var address in (string[])[verified, unverified])
        {
            (await CallAsync("register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        }

        host.Time.Advance(TimeSpan.FromHours(24));
        (await VerifyAsync(verified)).Status.ShouldBe(HttpStatusCode.NoContent);
        host.Time.Advance(TimeSpan.FromTicks(1));
        var expired = await VerifyAsync(unverified);
        expired.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        expired.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");
        (await MemberFixture.DirectLoginAsync(host, unverified, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);

        var (reset, kept) = (verified, (await host.CreateMemberAsync(password: Password, cancellationToken: Token)).Email);
        foreach (var address in (string[])[reset, kept])
        {
            (await CallAsync("forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        }

        host.Time.Advance(TimeSpan.FromHours(1));
        (await ResetAsync(reset)).Status.ShouldBe(HttpStatusCode.NoContent);
        host.Time.Advance(TimeSpan.FromTicks(1));
        var late = await ResetAsync(kept);
        late.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        late.Json["code"]!.GetValue<string>().ShouldBe("FCMS3166");

        (await MemberFixture.DirectLoginAsync(host, reset, MemberFixture.NewPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.DirectLoginAsync(host, kept, Password)).Status.ShouldBe(HttpStatusCode.OK);
    }

    // The owner registers first, and someone else registers the address before the owner verifies.
    [Fact]
    public async Task ARegistrationAfterTheOwnersSetsNoPasswordAndTheOwnerChoosesItWithTheMailedLink()
    {
        const string Owners = "The-Owners-First-Password-1";
        const string Intruders = "An-Intruders-Password-22";
        var address = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Owners}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var verification = fixture.TokenOf(address);
        var registered = await MemberAsync(address);

        var later = MemberMailLimiter.Window + TimeSpan.FromSeconds(1);
        fixture.Host.Time.Advance(later);
        var competing = await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Intruders}}", "displayName": "Intruder" }""");
        competing.Status.ShouldBe(HttpStatusCode.Accepted);
        competing.Body.ShouldBeNull();

        // Neither password is kept, the earlier link is dead, and the member counts as made now.
        var cleared = await MemberAsync(address);
        cleared.Id.ShouldBe(registered.Id);
        cleared.PasswordHash.ShouldBeNull();
        cleared.EmailConfirmed.ShouldBeFalse();
        cleared.DisplayName.ShouldBeNull();
        cleared.SecurityStamp.ShouldNotBe(registered.SecurityStamp);
        cleared.CreatedAt.ShouldBe(registered.CreatedAt + later);
        (await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "{{verification}}" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.DirectLoginAsync(address, Intruders)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Owners)).Status.ShouldBe(HttpStatusCode.BadRequest);

        // The mail goes to the owner of the mailbox, with the link that sets a password and verifies the address.
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Last(mail => mail.To == address).Subject.ShouldBe("Choose a password to finish registering");
        }

        fixture.Emails.LinkTo(address).GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/members/reset-password");
        var link = fixture.TokenOf(address);

        // A third registration, and one more, change nothing: the stamp stays, so the owner's link goes on working.
        foreach (var password in (string[])[Intruders, "Yet-Another-Password-333"])
        {
            fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
            (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
            var unchanged = await MemberAsync(address);
            unchanged.SecurityStamp.ShouldBe(cleared.SecurityStamp);
            unchanged.PasswordHash.ShouldBeNull();
            unchanged.CreatedAt.ShouldBe(cleared.CreatedAt);
            lock (fixture.Emails.Sent)
            {
                fixture.Emails.Sent.Last(mail => mail.To == address).Subject.ShouldBe("You already have an account");
            }
        }

        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{link}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await MemberAsync(address)).EmailConfirmed.ShouldBeTrue();
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.DirectLoginAsync(address, Intruders)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Owners)).Status.ShouldBe(HttpStatusCode.BadRequest);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var normalized = address.ToUpperInvariant();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().AsNoTracking().CountAsync(member => member.NormalizedEmail == normalized, Token)).ShouldBe(1);
    }

    // Someone else registers the address first, and the owner registers it afterwards.
    [Fact]
    public async Task ARegistrationBeforeTheOwnersNeverSignsInWhetherOrNotTheOwnerFinishes()
    {
        const string Intruders = "An-Intruders-Password-22";
        var address = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Intruders}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var intrudersLink = fixture.TokenOf(address);

        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);

        // The verification link of the first registration verifies nothing any more, and neither password signs in.
        (await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "{{intrudersLink}}" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.DirectLoginAsync(address, Intruders)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);

        // A new verification mail verifies the address and still gives nobody a password.
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        var chosen = fixture.TokenOf(address);
        (await fixture.MembersAsync(HttpMethod.Post, "resend-verification", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await fixture.MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DirectLoginAsync(address, Intruders)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{chosen}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The owner chooses the password with a link of its own mailbox.
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}", "password": "{{MemberFixture.NewPassword}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DirectLoginAsync(address, MemberFixture.NewPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.DirectLoginAsync(address, Intruders)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TwoRegistrationsOfAPendingAddressAtOnceAreBothAnsweredLikeAnyOther()
    {
        var address = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        var mails = fixture.MailsTo(address);

        // This request read the member before another registration cleared its password, so its own change is refused as
        // stale. It ends as the other did, with no error and no mail of its own.
        await using var stale = fixture.Host.Services.CreateAsyncScope();
        (await stale.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(address)).ShouldNotBeNull();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "Another-Registration-22" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        fixture.MailsTo(address).ShouldBe(mails + 1);
        var cleared = await MemberAsync(address);

        await stale.ServiceProvider.GetRequiredService<MemberAccountService>().RegisterAsync(address, "A-Stale-Registration-333", null, Token);

        fixture.MailsTo(address).ShouldBe(mails + 1);
        var after = await MemberAsync(address);
        after.SecurityStamp.ShouldBe(cleared.SecurityStamp);
        after.PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task ARegistrationNeverTakesOverAMemberAnAdministratorMadeNorKeepsTheGroupsOfTheRegistrationBefore()
    {
        // A member an administrator made, in a group, waits for its own link: a registration of its address gives it no password.
        var invited = MemberFixture.NewAddress();
        var created = await fixture.ManageAsync(HttpMethod.Post, "members", $$"""{ "email": "{{invited}}" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var invitedId = created.Json["id"]!.GetValue<string>();
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{invitedId}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);
        var setPassword = fixture.TokenOf(invited);

        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{invited}}", "password": "Someone-Elses-Password-1" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        lock (fixture.Emails.Sent)
        {
            fixture.Emails.Sent.Last(mail => mail.To == invited).Subject.ShouldBe("You already have an account");
        }

        // The administrator's link still works, and only its password signs in.
        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{invited}}", "token": "{{setPassword}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DirectLoginAsync(invited, "Someone-Elses-Password-1")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(invited, Password)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{invitedId}")).Json["groups"]!.AsArray().Count.ShouldBe(1);

        // A registration that is not verified and was put in a group: once its address is registered again it is in none.
        var pending = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "Someone-Elses-Password-1" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var pendingId = await fixture.MemberIdAsync(pending);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{pendingId:D}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);

        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{pendingId:D}")).Json["groups"]!.AsArray().ShouldBeEmpty();
        var left = (await fixture.ActivityAsync("member.group_changed", pendingId.ToString("D"))).Single(row => row.Details!.Contains("\"added\":\"\"", StringComparison.Ordinal));
        left.ActorType.ShouldBe(PrincipalKind.Member);
    }

    [Fact]
    public async Task ARequestThatIsWrongInItselfIsToldSoWhoeverSendsIt()
    {
        var known = await fixture.RegisterVerifiedAsync();

        foreach (var address in (string[])[known, MemberFixture.NewAddress()])
        {
            var weak = await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "short" }""");
            weak.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
            weak.Json["code"]!.GetValue<string>().ShouldBe("FCMS3165");
        }

        var bad = await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "not an address", "password": "{{Password}}" }""");
        bad.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        bad.Json["code"]!.GetValue<string>().ShouldBe("FCMS3164");

        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{MemberFixture.NewAddress()}}" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{MemberFixture.NewAddress()}}", "password": "{{Password}}", "role": "admin" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // A body that is not JSON is not read, so a form posted from another site registers nobody.
        using var client = fixture.Host.CreateSecureClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = MemberFixture.NewAddress(), ["password"] = Password });
        using var posted = await client.PostAsync(MemberFixture.Api + "register", form, Token);
        posted.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task MeAnswersTheMemberAndRefusesAnAdminTokenAndAVisitor()
    {
        var address = await fixture.RegisterVerifiedAsync(displayName: "First name");
        var member = new TestMember(await fixture.MemberIdAsync(address), address, Principal.Anonymous);
        using var client = await fixture.Host.AsMemberAsync(member);

        var me = await fixture.MembersAsync(HttpMethod.Get, "me", client: client);
        me.Status.ShouldBe(HttpStatusCode.OK, me.Body?.ToJsonString());
        me.Json["id"]!.GetValue<string>().ShouldBe(member.Id.ToString("D"));
        me.Json["displayName"]!.GetValue<string>().ShouldBe("First name");

        var renamed = await fixture.MembersAsync(HttpMethod.Patch, "me", """{ "displayName": "Second name" }""", client);
        renamed.Status.ShouldBe(HttpStatusCode.OK, renamed.Body?.ToJsonString());
        renamed.Json["displayName"]!.GetValue<string>().ShouldBe("Second name");
        (await fixture.MembersAsync(HttpMethod.Patch, "me", "{}", client)).Json["displayName"]!.GetValue<string>().ShouldBe("Second name");
        (await fixture.MembersAsync(HttpMethod.Patch, "me", """{ "displayName": "" }""", client)).Json["displayName"].ShouldBeNull();

        // Nothing but the display name is the member's to change here.
        (await fixture.MembersAsync(HttpMethod.Patch, "me", """{ "email": "other@example.com" }""", client)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.MembersAsync(HttpMethod.Patch, "me", $$"""{ "displayName": "{{new string('x', 129)}}" }""", client)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);

        foreach (var (method, body) in ((HttpMethod, string?)[])[(HttpMethod.Get, null), (HttpMethod.Patch, """{ "displayName": "Admin" }""")])
        {
            var admin = await fixture.MembersAsync(method, "me", body, fixture.Admin);
            admin.Status.ShouldBe(HttpStatusCode.Forbidden);
            admin.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

            var visitor = await fixture.MembersAsync(method, "me", body);
            visitor.Status.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await fixture.MembersAsync(HttpMethod.Post, "me/change-password", $$"""{ "currentPassword": "{{Password}}", "newPassword": "{{MemberFixture.NewPassword}}" }""", fixture.Admin)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AMemberChangesItsPasswordWhichEndsItsSessionsAndAWrongCurrentPasswordCountsTowardsTheLockout()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var tokens = await fixture.DirectLoginAsync(address, Password);
        using var client = fixture.Bearer(tokens.Json["access_token"]!.GetValue<string>());
        var change = $$"""{ "currentPassword": "{{Password}}", "newPassword": "{{MemberFixture.NewPassword}}" }""";

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var wrong = await fixture.MembersAsync(HttpMethod.Post, "me/change-password", $$"""{ "currentPassword": "not it {{attempt}}", "newPassword": "{{MemberFixture.NewPassword}}" }""", client);
            wrong.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
            wrong.Json["code"]!.GetValue<string>().ShouldBe("FCMS3167");
        }

        (await fixture.MembersAsync(HttpMethod.Post, "me/change-password", change, client)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: client)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(tokens.Json["refresh_token"]!.GetValue<string>(), MemberFixture.DirectClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, MemberFixture.NewPassword)).Status.ShouldBe(HttpStatusCode.OK);

        // The fifth wrong one locks the member out, and then even the right one is refused with the same answer.
        var next = await fixture.DirectLoginAsync(address, MemberFixture.NewPassword);
        using var again = fixture.Bearer(next.Json["access_token"]!.GetValue<string>());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await fixture.MembersAsync(HttpMethod.Post, "me/change-password", $$"""{ "currentPassword": "still not it {{attempt}}", "newPassword": "{{Password}}" }""", again)).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var locked = await fixture.MembersAsync(HttpMethod.Post, "me/change-password", $$"""{ "currentPassword": "{{MemberFixture.NewPassword}}", "newPassword": "{{Password}}" }""", again);
        locked.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        locked.Json["code"]!.GetValue<string>().ShouldBe("FCMS3167");
        (await fixture.DirectLoginAsync(address, MemberFixture.NewPassword)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegistrationAnswers404WhileItIsOffAndTheOtherRoutesGoOn()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails, configure: cms => cms.Members.AllowRegistration = false);
        using var client = host.CreateSecureClient();

        var refused = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{MemberFixture.NewAddress()}}", "password": "{{Password}}" }""");
        refused.Status.ShouldBe(HttpStatusCode.NotFound);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3104");
        emails.Count.ShouldBe(0);

        using var browser = new HostedPagesBrowser(host);
        using var page = await browser.GetAsync("/account/members/register");
        page.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await browser.PageAsync("/account/members/login")).ShouldNotContain("Register");

        // A member an administrator made still resets its password.
        var member = await host.CreateMemberAsync(password: Password, cancellationToken: Token);
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "forgot-password", $$"""{ "email": "{{member.Email}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        await AccountMailJobs.RunAsync(host);
        emails.Count.ShouldBe(1);

        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().CountAsync(Token)).ShouldBe(1);
    }

    [Fact]
    public async Task ALinkComesFromTheFrontEndsTemplateOrThePublicOriginAndNeverFromTheRequest()
    {
        // With a template, the link goes to the front end, with the address and the token escaped into it.
        var emails = new HostedPagesTests.CapturedEmails();
        await using (var host = MemberFixture.NewHost(emails, configure: cms =>
        {
            cms.Members.Links.VerifyEmail = "https://app.example.com/verify?e={email}#t={token}";
            cms.Members.Links.ResetPassword = "https://app.example.com/reset/{token}?address={email}";
        }))
        {
            var address = $"a+b{Guid.NewGuid():N}@example.com";
            using var forged = new HostedPagesBrowser(host, baseAddress: new Uri("https://evil.example/"));
            using var sent = await PostJsonAsync(forged, MemberFixture.Api + "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""");
            sent.StatusCode.ShouldBe(HttpStatusCode.Accepted);

            var link = emails.LinkTo(address);
            link.AbsoluteUri.ShouldStartWith("https://app.example.com/verify?e=" + Uri.EscapeDataString(address) + "#t=");
            link.AbsoluteUri.ShouldNotContain("evil.example");
        }

        // With the public origin alone, a forged Host header changes nothing.
        emails = new HostedPagesTests.CapturedEmails();
        await using (var host = MemberFixture.NewHost(emails))
        {
            var address = MemberFixture.NewAddress();
            using var forged = new HostedPagesBrowser(host, baseAddress: new Uri("https://evil.example/"));
            using var sent = await PostJsonAsync(forged, MemberFixture.Api + "register", $$"""{ "email": "{{address}}", "password": "{{Password}}" }""");
            sent.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            emails.LinkTo(address).Authority.ShouldBe("localhost");
        }

        // With neither, the answer is the same and no mail goes out, since the link would have to follow the request.
        emails = new HostedPagesTests.CapturedEmails();
        var logs = new LogCapture();
        await using (var host = MemberFixture.NewHost(emails, logs, cms => cms.PublicOrigin = null))
        {
            using var client = host.CreateSecureClient();
            (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{MemberFixture.NewAddress()}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
            emails.Count.ShouldBe(0);
            logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.NoMailLinkId && entry.Message.Contains("FCMS3173", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheJobRemovesTheMembersThatNeverVerifiedAndKeepsTheOthers()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails);
        using var client = host.CreateSecureClient();
        var stale = MemberFixture.NewAddress();
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{stale}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var contested = MemberFixture.NewAddress();
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{contested}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var verified = await host.CreateMemberAsync(cancellationToken: Token);

        host.Time.Advance(TimeSpan.FromDays(6));
        var recent = MemberFixture.NewAddress();
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{recent}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);

        // An address that is registered again counts as made then, so the link of that mail has its time.
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{contested}}", "password": "Another-Registration-22" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        host.Time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(DefaultSite.Id), DefaultSite.Locale, null, "cleanup", host.Time.GetUtcNow());
            await scope.ServiceProvider.GetRequiredService<UnverifiedMembersJob>().ExecuteAsync(
                new UnverifiedMembersPayload(),
                new JobContext(Guid.CreateVersion7(), UnverifiedMembersJob.Type, "default", 1, 3, operation, Token));
        }

        await using var check = host.Services.CreateAsyncScope();
        var left = await check.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().AsNoTracking().Select(member => member.Email).ToListAsync(Token);
        left.ShouldBe([verified.Email, recent, contested], ignoreOrder: true);
    }

    private async Task<CmsMember> MemberAsync(string address)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var normalized = address.ToUpperInvariant();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().AsNoTracking().SingleAsync(member => member.NormalizedEmail == normalized, Token);
    }

    private static string TokenOf(HostedPagesTests.CapturedEmails emails, string address) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(emails.LinkTo(address).Query)["token"].ToString();

    private static async Task<HttpResponseMessage> PostJsonAsync(HostedPagesBrowser browser, string url, string json)
    {
        // The browser posts forms; a JSON body goes through its client by hand.
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await browser.SendAsync(HttpMethod.Post, url, content);
    }
}
