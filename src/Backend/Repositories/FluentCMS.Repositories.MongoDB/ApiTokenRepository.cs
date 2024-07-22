

using DnsClient.Protocol;

namespace FluentCMS.Repositories.MongoDB;

public class ApiTokenRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ApiToken>(mongoDbContext, authContext),
    IApiTokenRepository
{
}
