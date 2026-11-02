using System.Text.RegularExpressions;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;

namespace FluentCMS.Core.Events;

// What a failed delivery stores and logs: the type of the exception and a capped message. A message often quotes the
// request that failed, so a URL keeps its host and path only, without credentials, query string or fragment, and the
// values of the event's own payload are scrubbed as they are for a job.
internal static partial class OutboxErrorText
{
    public static string Of(Exception exception, string payload)
    {
        var text = JobErrorText.ForStorage(exception, payload);
        return Cap(Url().Replace(text, "${scheme}${host}${path}"));
    }

    private static string Cap(string text) => text.Length <= Job.LastErrorMaxLength ? text : text[..Job.LastErrorMaxLength];

    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*://)(?:[^/?#@\s]*@)?(?<host>[^/?#\s]*)(?<path>[^?#\s]*)(?:[?#]\S*)?")]
    private static partial Regex Url();
}
