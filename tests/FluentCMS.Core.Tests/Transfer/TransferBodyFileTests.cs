using FluentCMS.Core.Transfer;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Transfer;

// The body of an import is kept in the shared temporary folder, so on Unix only its owner reads it, and it goes with the request.
public sealed class TransferBodyFileTests
{
    [Fact]
    public void TheBodyFileIsTheOwnersOnlyAndDeletedOnceClosed()
    {
        string path;
        using (var file = TransferImporter.CreateBodyFile())
        {
            path = file.Name;
            Path.GetDirectoryName(path).ShouldBe(Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        File.Exists(path).ShouldBeFalse();
    }
}
