using System.Text;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin.Api;

// The multipart/form-data body of POST /media with one file, written in one buffer with room for the file's bytes, so that
// a file is read straight into the body and held once. The half writes it, so the admin's first download keeps no
// multipart writer (spec 7, size budget).
internal sealed class MediaUploadBody
{
    private const string Crlf = "\r\n";

    private MediaUploadBody(byte[] buffer, int fileStart, int fileLength, string boundary)
    {
        File = buffer.AsMemory(fileStart, fileLength);
        Content = new ApiContent($"multipart/form-data; boundary={boundary}", buffer);
    }

    // Where the file's bytes go.
    public Memory<byte> File { get; }

    public ApiContent Content { get; }

    public static MediaUploadBody Create(Guid? folder, string fileName, long size)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        var boundary = "fluentcms-" + Guid.NewGuid().ToString("N");
        var head = new StringBuilder();
        if (folder is { } id)
        {
            head.Append("--").Append(boundary).Append(Crlf)
                .Append("Content-Disposition: form-data; name=\"folderId\"").Append(Crlf).Append(Crlf)
                .Append(id.ToString("D")).Append(Crlf);
        }

        // The name as ASCII for any reader, and in full as filename*, which ASP.NET Core prefers (RFC 7578, RFC 5987).
        head.Append("--").Append(boundary).Append(Crlf)
            .Append("Content-Disposition: form-data; name=\"files\"; filename=\"").Append(Ascii(fileName))
            .Append("\"; filename*=UTF-8''").Append(Uri.EscapeDataString(fileName)).Append(Crlf)
            .Append("Content-Type: application/octet-stream").Append(Crlf).Append(Crlf);
        var tail = Crlf + "--" + boundary + "--" + Crlf;

        var start = Encoding.UTF8.GetBytes(head.ToString());
        var end = Encoding.ASCII.GetBytes(tail);
        var buffer = new byte[checked(start.Length + (int)size + end.Length)];
        start.CopyTo(buffer, 0);
        end.CopyTo(buffer, start.Length + (int)size);
        return new MediaUploadBody(buffer, start.Length, (int)size, boundary);
    }

    private static string Ascii(string name) =>
        string.Create(name.Length, name, (chars, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                chars[index] = c is < ' ' or > '~' or '"' or '\\' ? '_' : c;
            }
        });
}
