using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Caching.Configuration;
using StackExchange.Redis;

namespace Caching.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Добавить зависимости пакеты Caching 
    /// </summary>
    public static void AddCaching<TCacheObject, TCacheDataProvider>(this IServiceCollection services, IConfiguration configuration) 
        where TCacheObject : class, ICacheObject
        where TCacheDataProvider: class, ICacheDataProvider<TCacheObject>
    {
        var redisConfiguration = configuration
            .GetRequiredSection(RedisConfiguration.ConfigurationSectionName)
            .Get<RedisConfiguration>();

        services.AddSingleton<ICacheDataProvider<TCacheObject>, TCacheDataProvider>();
        
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = redisConfiguration.EndPoints.Aggregate(
                new EndPointCollection(), (endPoints, endPoint) => { endPoints.Add(endPoint); return endPoints; }),
            Ssl = redisConfiguration.Ssl == true,
            User = redisConfiguration.User,
            Password = redisConfiguration.Password
        }));
        
        services.AddSingleton<IDistributedCacheHandler<RedisKey,RedisValue>, RedisCacheHandler>();
        services.AddSingleton<ILocalMemoryCache<TCacheObject>>(s =>
            new LocalMemoryCache<TCacheObject>(
                s.GetRequiredService<IDistributedCacheHandler<RedisKey, RedisValue>>(),
                s.GetRequiredService<ICacheDataProvider<TCacheObject>>(),
                s.GetRequiredService<ILogger<LocalMemoryCache<TCacheObject>>>()
            ));
    }
}