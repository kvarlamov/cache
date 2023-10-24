using StackExchange.Redis;

namespace Caching.Tests.TestData;

public class InMemoryCacheHandler : IDistributedCacheHandler<RedisKey, RedisValue>
{
    public Dictionary<RedisKey, RedisValue> DistributedStorage { get; private set; } = new();

    public Task SetAsync(RedisKey key, RedisValue value)
    {
        if (!DistributedStorage.TryAdd(key, value))
            DistributedStorage[key] = value;
        
        return Task.CompletedTask;
    }

    public Task SetAsync(KeyValuePair<RedisKey, RedisValue>[] data)
    {
        foreach (var kvp in data)
        {
            if (!DistributedStorage.TryAdd(kvp.Key, kvp.Value))
                DistributedStorage[kvp.Key] = kvp.Value;
        }

        return Task.CompletedTask;
    }

    public Task<RedisValue> GetAsync(RedisKey key)
    {
        return DistributedStorage.TryGetValue(key, out var value) ? Task.FromResult(value) : Task.FromResult(new RedisValue());
    }

    public async Task<RedisValue[]> GetAsync(RedisKey[] keys)
    {
        var res = new List<RedisValue>();
        foreach (var redisKey in keys)
        {
            res.Add(await GetAsync(redisKey));
        }

        return res.ToArray();
    }

    public Task<RedisKey[]> GetAllKeysByPattern(RedisValue pattern)
    {
        throw new NotImplementedException();
    }
}