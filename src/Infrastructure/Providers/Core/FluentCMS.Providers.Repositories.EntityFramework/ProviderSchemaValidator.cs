using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public class ProviderSchemaValidator(ProviderDbContext providerDbContext, ILogger<EfSchemaValidator<ProviderDbContext>> logger) : EfSchemaValidator<ProviderDbContext>(providerDbContext, logger)
{
    public override int Priority => 1;

}
