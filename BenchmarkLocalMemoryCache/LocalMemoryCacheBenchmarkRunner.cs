using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Caching;
using Caching.Tests.Entities;
using StackExchange.Redis;

namespace BenchmarkLocalMemoryCache;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class LocalMemoryCacheBenchmarkRunner
{
    private IDistributedCacheHandler<RedisKey, RedisValue> _distributedCacheHandler;
    private ICacheDataProvider<User> _cacheDataProvider;
    private LocalMemoryCache<User> _localMemoryCache;
    private static User[] _users;

    public static List<string> Keys;
    
    public static List<string> Names;
    
    public static List<string> Ids;

    public LocalMemoryCacheBenchmarkRunner()
    {
        _distributedCacheHandler = new RedisCacheHandler(ConnectionMultiplexer.Connect("localhost:6379"));
        _cacheDataProvider = new MartenCacheDataProvider();
        ILogger<LocalMemoryCache<User>> logger = new Logger<LocalMemoryCache<User>>(new NullLoggerFactory());
        _localMemoryCache = new(_distributedCacheHandler, _cacheDataProvider, logger);
        _localMemoryCache.ReloadAllObjectAsync().GetAwaiter().GetResult();
        if (_users == null || !_users.Any())
            _users = _cacheDataProvider.GetAllAsync().GetAwaiter().GetResult().ToArray();

        if (Keys == null || !Keys.Any()) Keys = _users.Select(x => x.ImmutabilityId.ToString()).ToList();
        if (Names == null || !Names.Any()) Names = _users.Select(x => x.FullName).ToList();
        if (Ids == null || !Ids.Any()) Ids = _users.Select(x => x.Id.ToString()).ToList();
    }

    [Benchmark(Description = "GetAllActual")]
    public async Task<User?[]> GetAllActual()
    {
        return (await _localMemoryCache.GetAllActualAsync()).ToArray();
    }
    
    [Benchmark(Description = "GetByKeyAsync")]
    public async Task<User?> GetByKey()
    {
        return await _localMemoryCache.GetByKeyAsync(Keys[0]);
    }
    
    [Benchmark(Description = "GetByName")]
    public async Task<User?> GetByName()
    {
        return await _localMemoryCache.GetByIndexAsync(nameof(User.FullName), Names[0]);
    }
    
    [Benchmark(Description = "GetById")]
    public async Task<User?> GetById()
    {
        return await _localMemoryCache.GetByIndexAsync(nameof(User.Id), Ids[0]);
    }

    [Benchmark(Description = "GetAllByKeys")]
    public async Task<List<User?>> GetAllByKeys()
    {
        return await _localMemoryCache.GetByKeysAsync(Keys.ToArray());
    }

    [Benchmark(Description = "GetByNames")]
    public async Task<List<User?>> GetAllByNames()
    {
        return await _localMemoryCache.GetByIndexesAsync(nameof(User.FullName), Names.ToArray());
    }
    
    [Benchmark(Description = "GetAllByIds")]
    public async Task<List<User?>> GetAllByIds()
    {
        return await _localMemoryCache.GetByIndexesAsync(nameof(User.Id), Ids.ToArray());
    }
}