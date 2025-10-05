using FluentCMS.Repositories;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public class ProviderSchemaValidator(ProviderDbContext providerDbContext, ILogger<ProviderSchemaValidator> logger) : BaseSchemaValidator<ProviderDbContext>(providerDbContext, logger)
{
    public override int Priority => 1;

}
