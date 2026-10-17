using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.Audit;

public sealed class AuditEventTests
{
    private static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");
    private static readonly AgentInitiator CatalogSync = new(Guid.NewGuid(), "catalog-sync");

    [Fact]
    public void A_user_event_names_no_agent_credential_and_leaves_unfilled_fields_empty()
    {
        var auditEvent = Event(Amir);

        Assert.Same(Amir, auditEvent.Initiator);
        Assert.Null(auditEvent.AgentCredentialId);
        Assert.Empty(auditEvent.Sources);
        Assert.Null(auditEvent.SubjectType);
        Assert.Null(auditEvent.SubjectId);
        Assert.Null(auditEvent.Model);
        Assert.Null(auditEvent.PromptHash);
        Assert.Null(auditEvent.ChangeSetId);
        Assert.Null(auditEvent.PreviousVersionId);
    }

    [Fact]
    public void An_agent_event_exposes_the_credential_it_acted_under()
    {
        var auditEvent = Event(CatalogSync);

        Assert.Equal(CatalogSync.CredentialId, auditEvent.AgentCredentialId);
    }

    [Fact]
    public void Every_AUD_01_field_is_kept()
    {
        var subjectId = Guid.NewGuid();
        var changeSetId = Guid.NewGuid();
        var previousVersionId = Guid.NewGuid();

        var auditEvent = Event(CatalogSync) with
        {
            SubjectType = "entry",
            SubjectId = subjectId,
            Model = "claude-sonnet-4-5",
            PromptHash = "sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            Sources = ["https://example.com/prices", "Dana, fact owner"],
            ChangeSetId = changeSetId,
            PreviousVersionId = previousVersionId,
        };

        Assert.Equal("entry", auditEvent.SubjectType);
        Assert.Equal(subjectId, auditEvent.SubjectId);
        Assert.Equal("claude-sonnet-4-5", auditEvent.Model);
        Assert.StartsWith("sha256:", auditEvent.PromptHash);
        Assert.Equal(["https://example.com/prices", "Dana, fact owner"], auditEvent.Sources);
        Assert.Equal(changeSetId, auditEvent.ChangeSetId);
        Assert.Equal(previousVersionId, auditEvent.PreviousVersionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_operation_is_rejected(string operation)
    {
        var exception = Assert.Throws<ArgumentException>(() => Event(Amir) with { Operation = operation });

        Assert.Equal("Operation", exception.ParamName);
    }

    [Fact]
    public void A_missing_operation_or_initiator_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Event(Amir) with { Operation = null! });
        Assert.Throws<ArgumentNullException>(() => Event(Amir) with { Initiator = null! });
    }

    [Fact]
    public void An_empty_id_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Event(Amir) with { Id = Guid.Empty });

        Assert.Equal("Id", exception.ParamName);
    }

    [Fact]
    public void A_blank_or_missing_source_list_entry_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Event(Amir) with { Sources = ["https://example.com", " "] });

        Assert.Equal("Sources", exception.ParamName);
        Assert.Throws<ArgumentNullException>(() => Event(Amir) with { Sources = null! });
    }

    private static AuditEvent Event(Initiator initiator)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            Initiator = initiator,
            Operation = "test.ran",
        };
    }
}
