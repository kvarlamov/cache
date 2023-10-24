using StackExchange.Redis;

namespace Caching;

public class RedisCacheHandler : IDistributedCacheHandler<RedisKey, RedisValue>
{
    private readonly IDatabase _redis;
    private readonly IServer _server;

    public RedisCacheHandler(IConnectionMultiplexer redisMultiplexer)
    {
        _redis = redisMultiplexer?.GetDatabase()!;
        _server = redisMultiplexer?.GetServer(redisMultiplexer.GetEndPoints().First());
    }

    public async Task SetAsync(RedisKey key, RedisValue value)
    {
        await _redis.StringSetAsync(key, value);
    }

    public async Task SetAsync(KeyValuePair<RedisKey, RedisValue>[] data)
    {
        await _redis.StringSetAsync(data);
    }

    public async Task<RedisValue> GetAsync(RedisKey key) => await _redis.StringGetAsync(key);

    public async Task<RedisValue[]> GetAsync(RedisKey[] keys) => await _redis.StringGetAsync(keys);

    public async Task<RedisKey[]> GetAllKeysByPattern(RedisValue pattern)
    {
        var keys = _server.KeysAsync(0, pattern);
        var keyList = new List<RedisKey>();
        await foreach (var key in keys)
        {
            keyList.Add(key);
        }

        return keyList.ToArray();
    }
}