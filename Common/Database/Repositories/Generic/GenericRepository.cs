using Common.Database.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Common.Database.Repositories.Generic;

public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class, IEntity
{
    protected readonly DbSet<TEntity> _dbSet;

    public GenericRepository(DbSet<TEntity> dbSet)
    {
        _dbSet = dbSet ?? throw new ArgumentNullException(nameof(dbSet));
    }

    public IQueryable<TEntity> Query()
    {
        return _dbSet.AsQueryable();
    }

    public async Task<TEntity?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<List<TEntity>> GetAllAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? filter = null,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsQueryable();

        query = filter is not null ? filter(query) : query;

        return await query.ToListAsync(ct);
    }

    public async Task<bool> AnyAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? filter = null,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsQueryable();

        query = filter is not null ? filter(query) : query;

        return await query.AnyAsync(ct);
    }

    public async Task<int> CountAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? filter = null,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsQueryable();

        query = filter is not null ? filter(query) : query;

        return await query.CountAsync(ct);
    }

    // решение: AddAsync нужен только для асинхронных генераторов ключей (например, HiLo); приложение их не использует, поэтому достаточно синхронного Add.
    public TEntity Create(TEntity entity)
    {
        return _dbSet.Add(entity).Entity;
    }

    public TEntity Update(TEntity entity)
    {
        return _dbSet.Update(entity).Entity;
    }

    public TEntity Delete(TEntity entity)
    {
        return _dbSet.Remove(entity).Entity;
    }
}
