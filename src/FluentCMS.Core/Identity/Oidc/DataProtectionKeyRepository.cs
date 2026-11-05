using System.Xml.Linq;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Identity.Oidc;

// The Data Protection key ring in cms_data_protection_key, so that every instance reads what another protected. A host
// that persists the ring elsewhere keeps its own repository.
internal sealed class DataProtectionKeyRepository(IServiceScopeFactory scopes, TimeProvider time) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        return [.. context.Set<DataProtectionKey>().AsNoTracking().Select(key => key.Xml).AsEnumerable().Select(XElement.Parse)];
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Add(new DataProtectionKey
        {
            Id = Guid.CreateVersion7(),
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAt = time.GetUtcNow(),
        });
        context.SaveChanges();
    }
}
