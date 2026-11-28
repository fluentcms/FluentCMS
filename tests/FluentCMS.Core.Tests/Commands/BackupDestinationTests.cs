using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Commands;

public sealed class BackupDestinationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fluentcms-destination-tests-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void WindowsExtendedNamesCannotHideEitherEndOfACollision()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows extended paths.");
        var db = Sentinel(_root, "database");
        var journal = Sentinel(_root, "database-journal");
        var media = _root.CreateSubdirectory("media");
        var missing = Path.Combine(media.FullName, "new", "backup.zip");
        foreach (var output in new[] { db, journal, db + "-wal", media.FullName, missing })
        {
            Refuses(Extended(output), db, media);
            Refuses(output, Extended(db), new DirectoryInfo(Extended(media.FullName)));
        }

        File.ReadAllText(db).ShouldBe("database");
        File.ReadAllText(journal).ShouldBe("database-journal");
        media.EnumerateFileSystemInfos().ShouldBeEmpty();
    }

    [Fact]
    public void UnixResolvesALinkBeforeDotDotInAnotherLinksTarget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX link traversal.");
        var protectedFolder = _root.CreateSubdirectory("protected");
        var nested = protectedFolder.CreateSubdirectory("nested");
        var db = Sentinel(protectedFolder, "database");
        var hop = Path.Combine(_root.FullName, "hop");
        var alias = Path.Combine(_root.FullName, "alias");
        Directory.CreateSymbolicLink(hop, nested.FullName);
        File.CreateSymbolicLink(alias, "hop/../database");
        File.ReadAllText(alias).ShouldBe("database");

        Refuses(alias, db, null);
        Refuses(db, alias, null);
        File.ReadAllText(db).ShouldBe("database");
    }

    [Fact]
    public void UnixProtectsTheLocationOfALinkAsWellAsItsTarget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX directory links.");
        var db = Sentinel(_root, "database");
        var media = _root.CreateSubdirectory("media");
        var outside = _root.CreateSubdirectory("elsewhere");
        var existing = Sentinel(outside, "archive");
        var link = Path.Combine(media.FullName, "out");
        Directory.CreateSymbolicLink(link, outside.FullName);
        Refuses(Path.Combine(link, "archive"), db, media);
        var back = Path.Combine(outside.FullName, "in");
        Directory.CreateSymbolicLink(back, media.FullName);
        Refuses(Path.Combine(back, "new", "backup.zip"), db, media);
        File.ReadAllText(existing).ShouldBe("archive");
    }

    [Fact]
    public void UnixRefusesAnUnresolvedLinkRatherThanTrustingItsSpelling()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX directory links.");
        var db = Sentinel(_root, "database");
        var link = Path.Combine(_root.FullName, "dangling");
        Directory.CreateSymbolicLink(link, "not-created");
        Refuses(Path.Combine(link, "archive"), db, null);
        File.ReadAllText(db).ShouldBe("database");
    }

    [Fact]
    public void TheCaseAliasesOfTheMountedVolumeAreProtected()
    {
        var db = Sentinel(_root, "database");
        var media = _root.CreateSubdirectory("media");
        var otherCase = Path.Combine(_root.FullName, "MEDIA");
        Assert.SkipUnless(Directory.Exists(otherCase), "This test directory is case sensitive.");
        Refuses(Path.Combine(_root.FullName, "DATABASE"), db, media);
        Refuses(Path.Combine(otherCase, "missing", "backup.zip"), db, media);
        File.ReadAllText(db).ShouldBe("database");
    }

    [Fact]
    public void OrdinaryExistingAndFutureSiblingArchivesRemainValid()
    {
        var db = Sentinel(_root, "database");
        var media = _root.CreateSubdirectory("media");
        var sibling = _root.CreateSubdirectory("media-backups");
        var existing = Sentinel(sibling, "archive");
        BackupDestination.Check(existing, db, media, Token);
        BackupDestination.Check(Path.Combine(sibling.FullName, "new", "archive"), db, media, Token);
        File.ReadAllText(existing).ShouldBe("archive");
        Directory.Exists(Path.Combine(sibling.FullName, "new")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public void AmbiguousUnicodeDatabaseAndSideFileNamesAreRefusedEvenBeforeFilesAreCreated(string suffix)
    {
        var db = Sentinel(_root, "straße.db");
        Refuses(Path.Combine(_root.FullName, "STRASSE.db" + suffix), db, null);
        File.ReadAllText(db).ShouldBe("straße.db");
        _root.GetFiles().Length.ShouldBe(1);
    }

    [Fact]
    public void AmbiguousUnicodeMediaRootsAndTheirDescendantsAreRefused()
    {
        var db = Sentinel(_root, "database");
        var media = _root.CreateSubdirectory("straße");
        var original = Sentinel(media, "original");
        var alias = Path.Combine(_root.FullName, "STRASSE");
        Refuses(alias, db, media);
        Refuses(Path.Combine(alias, "new", "archive"), db, media);
        File.ReadAllText(original).ShouldBe("original");
    }

    [Fact]
    public void KnownDistinctDirectoryEntriesBesideAUnicodeDatabaseRemainValid()
    {
        var db = Sentinel(_root, "straße.db");
        var archive = Sentinel(_root, "previous.zip");
        var media = _root.CreateSubdirectory("média");
        var sibling = _root.CreateSubdirectory("backups");
        BackupDestination.Check(archive, db, media, Token);
        BackupDestination.Check(Path.Combine(sibling.FullName, "future.zip"), db, media, Token);
        File.ReadAllText(archive).ShouldBe("previous.zip");
    }

    [Fact]
    public void TheTargetOfALinkedMediaFileIsNotAnArchiveDestination()
    {
        var db = Sentinel(_root, "database");
        var media = _root.CreateSubdirectory("media");
        var library = media.CreateSubdirectory("library");
        var target = Sentinel(_root.CreateSubdirectory("originals"), "previous.zip");
        var original = Path.Combine(library.FullName, "original.bin");
        try
        {
            File.CreateSymbolicLink(original, target);
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 1314)
        {
            Assert.Skip("File symlinks require Windows developer mode or the symlink privilege.");
        }

        Refuses(target, db, media);
        var safe = Sentinel(_root.CreateSubdirectory("backups"), "backup.zip");
        BackupDestination.Check(safe, db, media, Token).Select(file => file.FullName).ShouldBe([original]);
        File.ReadAllText(original).ShouldBe("previous.zip");
        File.ReadAllText(target).ShouldBe("previous.zip");
        File.ReadAllText(safe).ShouldBe("backup.zip");
    }

    private static string Extended(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    private static string Sentinel(DirectoryInfo directory, string name)
    {
        var path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, name);
        return path;
    }

    private static void Refuses(string output, string database, DirectoryInfo? media) =>
        Should.Throw<CommandException>(() => BackupDestination.Check(output, database, media, Token)).Code.ShouldBe(TransferCommandErrorCodes.UnsafeBackupDestination);
}
