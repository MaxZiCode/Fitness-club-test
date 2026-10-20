using System.Text.Json;
using StackExchange.Redis;

namespace Common.Caching;

public class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _connection;
    private readonly TimeSpan _defaultTtl;

    public RedisCacheService(IConnectionMultiplexer connection, TimeSpan defaultTtl)
    {
        _connection = connection;
        _defaultTtl = defaultTtl;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await _connection.GetDatabase().StringGetAsync(key).WaitAsync(ct);

        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!, SerializerOptions);
    }

    public async Task<T?[]> GetManyAsync<T>(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        var redisKeys = keys.Select(key => (RedisKey)key).ToArray();
        var values = await _connection.GetDatabase().StringGetAsync(redisKeys).WaitAsync(ct);

        return values
            .Select(value => value.IsNullOrEmpty
                ? default
                : JsonSerializer.Deserialize<T>(value!, SerializerOptions))
            .ToArray();
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(value, SerializerOptions);

        await _connection.GetDatabase().StringSetAsync(key, payload, ttl ?? _defaultTtl).WaitAsync(ct);
    }

    public async Task<long> IncrementAsync(string key, CancellationToken ct = default)
    {
        return await _connection.GetDatabase().StringIncrementAsync(key).WaitAsync(ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _connection.GetDatabase().KeyDeleteAsync(key).WaitAsync(ct);
    }
}
