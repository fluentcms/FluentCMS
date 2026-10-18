namespace FluentCMS.Core.Agents;

// The only place a key exists once it is issued. ToString leaves the key out, so logging the result cannot leak it (SEC-06).
public sealed record IssuedAgentCredential(AgentCredential Credential, string Key)
{
    public override string ToString()
    {
        return $"{nameof(IssuedAgentCredential)} {{ Credential = {Credential.Id}, Key = (hidden) }}";
    }
}
