using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Caching.Tests.Entities;
using StackExchange.Redis;

namespace Caching.PerformanceTests;

public class PerformanceTestRunner
{
    private IDistributedCacheHandler<RedisKey, RedisValue> _distributedCacheHandler;
    private ICacheDataProvider<User> _cacheDataProvider;
    private LocalMemoryCache<User> _localMemoryCache;
    private readonly int _numberOfThreads;
    private const string FilePath = "result.txt";
    
    private static User[] _users;

    private static List<string> _keys;
    
    private static List<string> _names;
    
    private static List<string> _ids;
    
    static Random random = new();
    

    public PerformanceTestRunner(int numberOfThreads)
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        _numberOfThreads = numberOfThreads;
        _distributedCacheHandler = new RedisCacheHandler(ConnectionMultiplexer.Connect("localhost:6379"));
        _cacheDataProvider = new CacheDataProviderDecorator();
        ILogger<LocalMemoryCache<User>> logger = new Logger<LocalMemoryCache<User>>(new NullLoggerFactory());
        _localMemoryCache = new(_distributedCacheHandler, _cacheDataProvider, logger);
        Console.WriteLine("Начали прогревать кэш");
        _localMemoryCache.ReloadAllObjectAsync().GetAwaiter().GetResult();
        
        if (_users == null || !_users.Any())
            _users = _cacheDataProvider.GetAllAsync().GetAwaiter().GetResult().ToArray();
        if (_keys == null || !_keys.Any()) _keys = _users.Select(x => x.ImmutabilityId.ToString()).ToList();
        if (_names == null || !_names.Any()) _names = _users.Select(x => x.FullName).ToList();
        if (_ids == null || !_ids.Any()) _ids = _users.Select(x => x.Id.ToString()).ToList();
        
        Console.WriteLine("Закончили прогревать кэш");
    }

    public async Task GetAllActualTest()
    {
        Console.WriteLine($"\n--Метод {nameof(GetAllActualTest)}");
        var sw = new Stopwatch();
        sw.Start();
        var res = await GetOnlyTests(GetAllActualAsync);
        sw.Stop();

        PrintResult(sw, nameof(GetAllActualTest));
    }

    public async Task GetAllByKeysTest()
    {
        Console.WriteLine($"\n--Метод {nameof(GetAllByKeysTest)}");
        var sw = new Stopwatch();
        sw.Start();
        var res = await GetOnlyTests(GetByKeysAsync);
        sw.Stop();

        PrintResult(sw, nameof(GetAllByKeysTest));
    }
    
    public async Task GetAllByNamesTest()
    {
        Console.WriteLine($"\n--Метод {nameof(GetAllByNamesTest)}");
        var sw = new Stopwatch();
        sw.Start();
        var res = await GetOnlyTests(GetByNamesAsync);
        sw.Stop();

        PrintResult(sw, nameof(GetAllByNamesTest));
    }
    
    public async Task GetAllByIdsTest()
    {
        Console.WriteLine($"\n--Метод {nameof(GetAllByIdsTest)}");
        var sw = new Stopwatch();
        sw.Start();
        var res = await GetOnlyTests(GetByIdsAsync);
        sw.Stop();

        PrintResult(sw, nameof(GetAllByIdsTest));
    }

    public async Task GetAndSet()
    {
        Console.WriteLine($"\n--Метод {nameof(GetAndSet)}");
        var sw = new Stopwatch();
        sw.Start();
        Task<List<IEnumerable<User?>>> getTask = GetOnlyTests(GetAllActualAsync);
        Task setTask = Set();

        await Task.WhenAll(getTask, setTask);
        sw.Stop();

        PrintResult(sw, nameof(GetAndSet));
    }

    private async Task<List<IEnumerable<User?>>> GetOnlyTests(Func<Task<IEnumerable<User?>>> func)
    {
        var users = new List<Task<IEnumerable<User?>>>();
        int tasksStarted = 0;

        while (tasksStarted < _numberOfThreads)
        {
            int tasksToStart = 50;
            for (var i = tasksStarted; i < tasksStarted + tasksToStart && i < _numberOfThreads; i++)
            {
                users.Add(func());
            }
            tasksStarted += tasksToStart;
        }

        await Task.WhenAll(users);
        
        var results = new List<IEnumerable<User?>>();

        foreach (var userTask in users)
        {
            results.Add(userTask.Result);
        }

        return results;
    }

    private async Task Set()
    {
        var users = new Task[100];
        Random rnd = new Random();
        for (int i = 0; i < users.Length; i++)
        {
            var userIndex = rnd.Next(0, _users.Length - 1);
            var user = _users[userIndex];
            
            users[i] = ReloadObject(user.ImmutabilityId.ToString());
        }

        await Task.WhenAll(users);
    }

    private async Task<IEnumerable<User?>> GetAllActualAsync()
    {
        return await _localMemoryCache.GetAllActualAsync();
    }

    private async Task<IEnumerable<User?>> GetByKeysAsync()
    {
        var values = GetRandomValues(_keys, random.Next(1, _keys.Count - 1));
        return await _localMemoryCache.GetByKeysAsync(values.ToArray());
    }
    
    private async Task<IEnumerable<User?>> GetByNamesAsync()
    {
        var values = GetRandomValues(_names, random.Next(1, _names.Count - 1));
        
        return await _localMemoryCache.GetByIndexesAsync(nameof(User.FullName), values.ToArray());
    }
    
    private async Task<IEnumerable<User?>> GetByIdsAsync()
    {
        var values = GetRandomValues(_ids, random.Next(1, _ids.Count - 1));
        
        return await _localMemoryCache.GetByIndexesAsync(nameof(User.Id), values.ToArray());
    }

    private async Task ReloadObject(string key)
    {
        await _localMemoryCache.ReloadObjectAsync(key);
    }

    private List<string> GetRandomValues(List<string> source, int numberOfValues)
    {
        Random rnd = new Random();
        var randomValues = new List<string>();
        for (int i = 0; i < numberOfValues; i++)
        {
            int randomIndex = rnd.Next(0, source.Count - 1);
            randomValues.Add(source[randomIndex]);
        }

        return randomValues;
    }

    private void PrintResult(Stopwatch sw, string methodName)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"\n--Результаты метода {methodName}");
        sb.Append($"\nКоличество пользователей: {_numberOfThreads}. " +
                  $"\nОбщее время выполнения метода {methodName} = {sw.ElapsedMilliseconds} мс." +
                  $"\nСреднее время вызова метода {methodName} одним пользователем = {(decimal)sw.ElapsedMilliseconds / _numberOfThreads} мс." +
                  $"\nКоличество обращений в БД = {CacheDataProviderDecorator.NumberOfDbCall}");
        Console.WriteLine(sb.ToString());
        
        using (StreamWriter writer = new StreamWriter(FilePath, true))
        {
            writer.WriteLine(sb.ToString());
        }

        CacheDataProviderDecorator.NumberOfDbCall = 0;
    }
}