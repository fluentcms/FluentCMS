using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using static FluentCMS.Tests.Agents.TestAgentCredentials;

namespace FluentCMS.Tests.Agents;

public sealed class AgentCredentialTests
{
    [Fact]
    public void An_issued_credential_keeps_its_name_scope_caps_and_times()
    {
        var scope = Scope();
        var caps = Caps();
        var expiresAt = new DateTimeOffset(2027, 1, 16, 14, 0, 0, TimeSpan.FromHours(2));

        var credential = AgentCredential.Issue(new NewAgentCredential("  catalog-sync ", expiresAt, scope, caps), Now).Value;

        Assert.NotEqual(Guid.Empty, credential.Id);
        Assert.Equal("catalog-sync", credential.DisplayName);
        Assert.Same(scope, credential.Scope);
        Assert.Equal(caps, credential.Caps);
        Assert.Equal(Now, credential.IssuedAt);
        Assert.Equal(expiresAt, credential.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, credential.ExpiresAt.Offset);
        Assert.Null(credential.RevokedAt);
    }

    [Fact]
    public void The_credential_is_the_agent_the_audit_log_names()
    {
        var credential = Issue();

        Assert.Equal(new AgentInitiator(credential.Id, "catalog-sync"), credential.Initiator);
    }

    [Fact]
    public void Every_problem_with_a_new_credential_is_reported_at_once()
    {
        var result = AgentCredential.Issue(New(" ", Now), Now);

        Assert.Equal(
            [AgentCredentialErrorCodes.DisplayNameMissing, AgentCredentialErrorCodes.ExpiryNotInFuture],
            result.Errors.Select(error => error.Code));
    }

    [Fact]
    public void A_display_name_fits_the_initiator_columns()
    {
        Assert.True(AgentCredential.Issue(New(new string('a', AgentCredential.MaxDisplayNameLength)), Now).IsSuccess);

        var result = AgentCredential.Issue(New(new string('a', AgentCredential.MaxDisplayNameLength + 1)), Now);
        Assert.Equal(AgentCredentialErrorCodes.DisplayNameTooLong, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_credential_is_active_until_it_expires()
    {
        var credential = Issue();

        Assert.True(credential.CheckActive(Now).IsSuccess);
        Assert.True(credential.CheckActive(credential.ExpiresAt.AddTicks(-1)).IsSuccess);
        Assert.Equal(AgentCredentialErrorCodes.Expired, Assert.Single(credential.CheckActive(credential.ExpiresAt).Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.Expired, Assert.Single(credential.CheckActive(credential.ExpiresAt.AddDays(1)).Errors).Code);
    }

    [Fact]
    public void A_revoked_credential_is_refused_at_once_and_cannot_be_revoked_again()
    {
        var credential = Issue();
        var revokedAt = Now.AddHours(1);

        Assert.True(credential.Revoke(revokedAt).IsSuccess);

        Assert.Equal(revokedAt, credential.RevokedAt);
        Assert.Equal(AgentCredentialErrorCodes.Revoked, Assert.Single(credential.CheckActive(revokedAt).Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.AlreadyRevoked, Assert.Single(credential.Revoke(revokedAt.AddHours(1)).Errors).Code);
        Assert.Equal(revokedAt, credential.RevokedAt);
    }

    [Fact]
    public void Revocation_is_reported_before_expiry()
    {
        var credential = Issue();
        credential.Revoke(Now);

        Assert.Equal(AgentCredentialErrorCodes.Revoked, Assert.Single(credential.CheckActive(credential.ExpiresAt.AddDays(1)).Errors).Code);
    }

    [Fact]
    public void An_expired_credential_can_still_be_revoked()
    {
        var credential = Issue();

        Assert.True(credential.Revoke(credential.ExpiresAt.AddDays(1)).IsSuccess);
    }

    [Fact]
    public void Restore_keeps_a_stored_credential_without_checking_it()
    {
        var id = Guid.NewGuid();

        var credential = AgentCredential.Restore(id, "", Scope(), Caps(), Now, Now.AddDays(-1), Now);

        Assert.Equal(id, credential.Id);
        Assert.Equal("", credential.DisplayName);
        Assert.Equal(Now, credential.RevokedAt);
    }

    [Fact]
    public void The_issue_result_never_prints_its_key()
    {
        var issued = new IssuedAgentCredential(Issue(), AgentKeys.Generate());

        Assert.DoesNotContain(issued.Key, issued.ToString(), StringComparison.Ordinal);
        Assert.Contains(issued.Credential.Id.ToString(), issued.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Key, $"{issued}", StringComparison.Ordinal);
    }
}
