using System.Security.Cryptography;

namespace FluentCMS.IntegrationTests.Media;

// Uploads are checked against the signature of their declared type, so test files start like the type they claim.
internal static class MediaFiles
{
    public static byte[] Jpeg(int length = 16)
    {
        var bytes = RandomNumberGenerator.GetBytes(Math.Max(length, 3));
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }
}
