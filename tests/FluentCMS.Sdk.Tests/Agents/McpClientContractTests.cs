using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Events;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Agents;

// The contracts of MCP client registration: what a value that says nothing means.
public class McpClientContractTests
{
    // An enum that was never set must not open anything: no registration, and a client that waits.
    [Fact]
    public void TheValuesThatSayNothingAreTheClosedOnes()
    {
        default(McpClientRegistrationMode).ShouldBe(McpClientRegistrationMode.Off);
        default(McpClientStatus).ShouldBe(McpClientStatus.Pending);
        default(McpClientChangeOutcome).ShouldBe(McpClientChangeOutcome.Done);
    }

    [Fact]
    public void APolicyNamesNoRoleAndCapsRegistrationsUnlessItSaysOtherwise()
    {
        var policy = new McpClientRegistrationPolicy(McpClientRegistrationMode.Approval);

        policy.DefaultRole.ShouldBeNull();
        policy.MaxPendingClients.ShouldBe(100);
        policy.RegistrationsPerHour.ShouldBe(10);
    }

    [Fact]
    public void AClientHasNoRoleApprovalOrActivityUntilItIsGivenOne()
    {
        var client = new McpClient(Guid.CreateVersion7(), "mcp-1", "Agent", McpClientStatus.Pending, ["https://agent.example.com/cb"], DateTimeOffset.UnixEpoch);

        client.RoleId.ShouldBeNull();
        client.RoleName.ShouldBeNull();
        client.Scopes.ShouldBeEmpty();
        client.ApprovedBy.ShouldBeNull();
        client.ApprovedAt.ShouldBeNull();
        client.LastActivityAt.ShouldBeNull();
    }

    [Fact]
    public void ARegistrationEventSurvivesTheJsonRoundTripOfTheOutbox()
    {
        var registered = new McpClientRegisteredEvent(Guid.CreateVersion7(), "mcp-1", "Agent", "pending", null, "203.0.113.7");

        var json = JsonSerializer.Serialize(registered, JsonSerializerOptions.Web);
        var read = JsonSerializer.Deserialize<McpClientRegisteredEvent>(json, JsonSerializerOptions.Web).ShouldNotBeNull();

        JsonSerializer.Serialize(read, JsonSerializerOptions.Web).ShouldBe(json);
        read.EventId.ShouldBe(registered.EventId);
        read.Ip.ShouldBe("203.0.113.7");
        read.RoleId.ShouldBeNull();
    }
}
