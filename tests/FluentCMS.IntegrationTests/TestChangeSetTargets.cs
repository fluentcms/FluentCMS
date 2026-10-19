using FluentCMS.Core.ChangeSets;

namespace FluentCMS.IntegrationTests;

internal static class TestChangeSetTargets
{
    public const string ContentModelSession = "content-model";

    // The session a test's content model is written in when the test is not about the change set itself (SCH-05).
    public static readonly ChangeSetTarget ContentModel = ChangeSetTarget.Session(
        ContentModelSession,
        new NewChangeSet("Define the content model.", []));
}
