using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;

namespace FluentCMS.Core.Events;

// What a failed delivery stores and logs: the type of the exception and a capped message, scrubbed of what looks like a
// secret, and of the values of the event's own payload as a job's error is.
internal static class OutboxErrorText
{
    public static string Of(Exception exception, string payload) =>
        Cap(SecretScrubber.Of(JobErrorText.ForStorage(exception, payload)));

    public static string Cap(string text) => text.Length <= Job.LastErrorMaxLength ? text : text[..Job.LastErrorMaxLength];
}
