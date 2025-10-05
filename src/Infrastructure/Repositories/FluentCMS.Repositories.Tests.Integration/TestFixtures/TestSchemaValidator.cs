using FluentCMS.Repositories.DataInitialization.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

public class TestSchemaValidator(TestDbContext dbContext, ILogger<TestSchemaValidator> logger) : BaseSchemaValidator<TestDbContext>(dbContext, logger)
{
    public override int Priority => 0;
}
