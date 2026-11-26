using System.Globalization;
using FluentCMS.Security;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The generated URL signing key, for `fluentcms keyring reprotect`. Protecting it again keeps the key, so the URLs signed
// before stay valid and an instance that holds the key in memory goes on as it is. A write names the value it read.
internal sealed class MediaProtectedValues(PluginDbContext<MediaPlugin> database) : IProtectedValueSource
{
    public const string KeyColumn = "key";

    public async Task<IReadOnlyList<ProtectedValue>> ReadAsync(CancellationToken cancellationToken)
    {
        var stored = await database.Set<SigningKey>().AsNoTracking()
            .Where(key => key.Id == MediaSigning.KeyId)
            .Select(key => key.Key)
            .FirstOrDefaultAsync(cancellationToken);
        return stored is { Length: > 0 }
            ? [new ProtectedValue(MediaModel.SigningKeyTable, MediaSigning.KeyId.ToString(CultureInfo.InvariantCulture), KeyColumn, MediaSigning.Purpose, stored)]
            : [];
    }

    public async Task<bool> WriteAsync(ProtectedValue value, string reprotected, CancellationToken cancellationToken)
    {
        var stored = value.Stored;
        return await database.Set<SigningKey>()
            .Where(key => key.Id == MediaSigning.KeyId && key.Key == stored)
            .ExecuteUpdateAsync(set => set.SetProperty(key => key.Key, reprotected), cancellationToken) > 0;
    }
}
