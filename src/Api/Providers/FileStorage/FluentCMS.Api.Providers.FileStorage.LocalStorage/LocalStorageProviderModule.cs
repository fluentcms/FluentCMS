using FluentCMS.Api.Providers.FileStorage.Abstractions;
using FluentCMS.Infrastructure.Providers.Abstractions;

namespace FluentCMS.Api.Providers.FileStorage.LocalStorage;

public class LocalStorageProviderModule : ProviderModuleBase<LocalFileStorageProvider, LocalFileStorageOptions>
{
    public override string Area => IFileStorageProvider.Area;

    public override string DisplayName => "Local File Storage Provider";
}
