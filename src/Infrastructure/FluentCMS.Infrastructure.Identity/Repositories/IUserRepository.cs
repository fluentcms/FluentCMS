namespace FluentCMS.Infrastructure.Identity.Repositories;

public interface IUserRepository<TUser> : IRepository<TUser> where TUser : class
{
}
