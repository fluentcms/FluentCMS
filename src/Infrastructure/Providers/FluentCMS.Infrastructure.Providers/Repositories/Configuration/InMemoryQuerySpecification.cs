namespace FluentCMS.Infrastructure.Providers.Repositories.Configuration;

public class InMemoryQuerySpecification<T>(IEnumerable<T> items) : IQuerySpecification<T>
    where T : class
{
    public Task<bool> Any(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Any());
    }

    public Task<bool> Any(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Any(predicate.Compile()));
    }

    public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetAsyncEnumerable(items, cancellationToken);
    }

    private static async IAsyncEnumerable<T> GetAsyncEnumerable(IEnumerable<T> source, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    public Task<int> Count(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count(predicate.Compile()));
    }

    public Task<int> Count(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count());
    }

    public IQuerySpecification<T> Distinct()
    {
        return new InMemoryQuerySpecification<T>(items.Distinct());
    }

    public Task<T> First(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.First());
    }

    public Task<T> First(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.First(predicate.Compile()));
    }

    public Task<T?> FirstOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.FirstOrDefault(predicate.Compile()));
    }

    public Task<T?> FirstOrDefault(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.FirstOrDefault());
    }

    public IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new InMemoryQuerySpecification<T>(items.GroupBy(keySelector.Compile()).Select(g => g.First()));
    }

    public IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new InMemoryQuerySpecification<T>(items.OrderBy(keySelector.Compile()));
    }

    public IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new InMemoryQuerySpecification<T>(items.OrderByDescending(keySelector.Compile()));
    }

    public IQuerySpecification<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class
    {
        return new InMemoryQuerySpecification<TResult>(items.Select(selector.Compile()));
    }

    public Task<T> Single(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Single());
    }

    public Task<T> Single(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Single(predicate.Compile()));
    }

    public Task<T?> SingleOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.SingleOrDefault(predicate.Compile()));
    }

    public Task<T?> SingleOrDefault(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.SingleOrDefault());
    }

    public IQuerySpecification<T> Skip(int count)
    {
        return new InMemoryQuerySpecification<T>(items.Skip(count));
    }

    public IQuerySpecification<T> Take(int count)
    {
        return new InMemoryQuerySpecification<T>(items.Take(count));
    }

    public IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new InMemoryQuerySpecification<T>(items.OrderBy(keySelector.Compile()));
    }

    public IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new InMemoryQuerySpecification<T>(items.OrderByDescending(keySelector.Compile()));
    }

    public Task<T[]> ToArray(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.ToArray());
    }


    public Task<List<T>> ToList(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.ToList());
    }

    public Task<IPagedResult<T>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var itemsList = items.ToList();
        return Task.FromResult<IPagedResult<T>>(new PagedResult<T>(
            itemsList.Skip((page - 1) * pageSize).Take(pageSize),
            itemsList.Count(),
            page,
            pageSize));
    }

    public IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)
    {
        return new InMemoryQuerySpecification<T>(items.Where(predicate.Compile()));
    }
}
