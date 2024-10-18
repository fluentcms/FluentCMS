using FluentCMS.Repositories.Postgres.Configurations.Base;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Repositories.Postgres.Configurations.UserAssociatedEntities;

public class IdentityUserTokenConfiguration : ShadowKeyEntityBaseConfiguration<IdentityUserToken<Guid>>
{

}
