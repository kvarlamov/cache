using BenchmarkLocalMemoryCache;
using Caching.Tests.Entities;

namespace Caching.PerformanceTests;

public class CacheDataProviderDecorator : ICacheDataProvider<User>
{
    public static int NumberOfDbCall { get; set; }

    private readonly ICacheDataProvider<User> _martenCacheDataProvider = new MartenCacheDataProvider();

    public async Task<User?> GetDataAsync(string key)
    {
        NumberOfDbCall++;
        return await _martenCacheDataProvider.GetDataAsync(key);
    }

    public async Task<IEnumerable<User>> GetDataAsync(string[] keys)
    {
        NumberOfDbCall++;
        return await _martenCacheDataProvider.GetDataAsync(keys);
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        if ((new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod().Name != "Start")
        {
            NumberOfDbCall++;
            PrintMethodCall(nameof(GetAllAsync));
        }
        
        return await _martenCacheDataProvider.GetAllAsync();
    }
    
    private void PrintMethodCall(string methodName) => Console.WriteLine($"Вызов {methodName}");
}