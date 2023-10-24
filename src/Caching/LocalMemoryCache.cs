using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Caching.Attributes;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Caching;

//todo НЕОБХОДИМО ПРОДУМАТЬ МЕХАНИКУ MASTER-SLAVE для ReloadAllObjectAsync,
//чтобы разные инстансы одновременно не перезагружали объекты в Redis

/// <summary>
/// Кэш
/// </summary>
/// <typeparam name="TCacheObject">Кэшируемый объект</typeparam>
public class LocalMemoryCache<TCacheObject> : ILocalMemoryCache<TCacheObject>
    where TCacheObject : class, ICacheObject
{
    private const string ReloadAllSuffix = "ReloadAll";
    
    // ключ, в значении которого записано время загрузки всех объектов на сервер распределенного кэширования
    private readonly string _redisReloadAllTimeKey;

    private readonly TimeSpan _criticalDelayToAvoidDeadLock = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, TCacheObject> _memory;
    
    private readonly SemaphoreSlim _lockReloadAllObjects = new(1);
    private readonly SemaphoreSlim _lockLoadAllActual = new(1);
    private readonly SemaphoreSlim _lockReloadInMemory = new(1);
    private readonly SemaphoreSlim _lockReloadObject = new(1);
    private readonly SemaphoreSlim _lockGetRedisValues = new(500, 500);

    private readonly string _redisPrefix;
    private readonly string? _redisKeyField;
    
    private readonly Dictionary<string, PropertyInfo> _indexProperties;
    
    private readonly IDistributedCacheHandler<RedisKey, RedisValue> _distributedCacheHandler;
    private readonly ICacheDataProvider<TCacheObject> _cacheDataProvider;
    private readonly ILogger<LocalMemoryCache<TCacheObject>> _logger;

    public LocalMemoryCache(IDistributedCacheHandler<RedisKey, RedisValue> distributedCacheHandler, ICacheDataProvider<TCacheObject> cacheDataProvider, ILogger<LocalMemoryCache<TCacheObject>> logger)
    {
        _memory = new ConcurrentDictionary<string, TCacheObject>();
        _distributedCacheHandler = distributedCacheHandler;
        _cacheDataProvider = cacheDataProvider;
        _logger = logger;
        _redisPrefix = typeof(TCacheObject).Name;
        _redisKeyField = GetKeyName();
        _redisReloadAllTimeKey = $"{_redisPrefix}.{ReloadAllSuffix}";
        _indexProperties = GetIndexedPropertiesWithNames();
    }
    
    /// <summary>
    /// Загружает все объекты в кэш и устанавливает в Redis значение ключа _redisReloadAllTimeKey
    /// </summary>
    /// <remarks>
    /// </remarks>
    public async Task ReloadAllObjectAsync()
    {
        await _lockReloadAllObjects.WaitAsync(_criticalDelayToAvoidDeadLock);
        try
        {
            if (!await IsRedisNeedReload() && !await IsLocalMemoryNeedReload())
                return;

            IEnumerable<TCacheObject> newObjects = await _cacheDataProvider.GetAllAsync();
            await LoadToCache(newObjects, true);
        }
        catch(Exception ex)
        {
            _logger?.LogError(ex, $"Caching. Произошла ошибка в методе {nameof(ReloadAllObjectAsync)}");
            throw;
        }
        finally
        {
            _lockReloadAllObjects.Release();
        }
    }

    public async Task ReloadObjectAsync(string key) => await ReloadObject(key, null);
    
    /// <summary>
    /// Получает объект по ключу, определяемому атрибутом <see cref="CacheKeyAttribute"/>
    /// </summary>
    /// <param name="key">ключ, определяемый атрибутом <see cref="CacheKeyAttribute"/></param>
    /// <returns>Кэшируемый объект или null</returns>
    public async Task<TCacheObject?> GetByKeyAsync(string key)
    {
        await ReloadAllIfCacheNotFilled();

        try
        {
            var redisValue = await GetRedis(_redisKeyField, key);
            if (redisValue == null) return await ReloadObject(key, null);

            var fromMemory = GetFromMemory(key);

            if (fromMemory == default) return await ReloadObject(key, redisValue.Version);

            return redisValue.Version != fromMemory.Version 
                ? await ReloadObjectInMemory(key, redisValue.Version) 
                : _memory[key];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Caching. Произошла ошибка в методе {nameof(GetByKeyAsync)}");
            throw;
        }
    }

    /// <summary>
    /// Получить по набору ключей
    /// </summary>
    /// <param name="keys">Набор ключей</param>
    /// <param name="fieldName">Поле, по которому осуществляется поиск. По умолчанию поле, помеченное <see cref="CacheKeyAttribute"/></param>
    /// <param name="needToFormatKeys">Необходимость форматировать ключ (при получении всех ключей из Redis через GetAllKeysByPattern форматирование не нужно)</param>
    /// <returns>Набор кэшируемых объектов</returns>
    public async Task<List<TCacheObject?>> GetByKeysAsync(string[] keys, string? fieldName = null, bool needToFormatKeys = true)
    {
        await ReloadAllIfCacheNotFilled();
        
        try
        {
            var field = string.IsNullOrEmpty(fieldName) ? _redisKeyField : fieldName;
    
            var redisValues = await GetRedis(field, keys, needToFormatKeys);
            var result = new List<TCacheObject?>();
            if (redisValues is not { Length: > 0 }) return result;

            var listToReload = new List<string>();
            foreach (var redisValue in redisValues)
            {
                var fromMemory = GetFromMemory(redisValue.Id);

                if (fromMemory == default)
                {
                    listToReload.Add(redisValue.Id);
                    continue;
                }

                if (redisValue.Version != fromMemory.Version)
                {
                    listToReload.Add(redisValue.Id);
                }
                else
                {
                    result.Add(_memory[redisValue.Id]);
                }
            }

            if (listToReload.Any())
            {
                var reloadedObjects = await ReloadObject(listToReload.ToArray());
                result.AddRange(reloadedObjects);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Caching. Произошла ошибка в методе {nameof(GetByKeysAsync)}");
            throw;
        }
    }

    public async Task<List<TCacheObject?>> GetByIndexesAsync(string fieldName, string[] keys) =>
        await GetByKeysAsync(keys, fieldName);
    
    /// <summary>
    /// Получает кэшируемый объект по полю, определяемому атрибутом <see cref="CacheIndexAttribute"/>
    /// </summary>
    /// <param name="fieldName">Наименование поля для поиска в кэше, определяемое атрибутом <see cref="CacheIndexAttribute"/></param>
    /// <param name="fieldValue">Значение поля для поиска в кэше</param>
    /// <returns></returns>
    public async Task<TCacheObject?> GetByIndexAsync(string? fieldName, string fieldValue)
    {
        await ReloadAllIfCacheNotFilled();
        
        try
        {
            var redisValue = await GetRedis(fieldName, fieldValue);
            if (redisValue == null)
                return default;

            var fromMemory = GetFromMemory(redisValue.Id);

            if (fromMemory == default) return (await ReloadObject(redisValue.Id, redisValue.Version))!;

            return redisValue.Version != fromMemory.Version
                ? (await ReloadObjectInMemory(redisValue.Id, redisValue.Version))!
                : _memory[redisValue.Id];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Caching. Произошла ошибка в методе {nameof(GetByIndexAsync)}");
            throw;
        }
    }

    /// <summary>
    /// Получить все актуальные объекты из кэша
    /// </summary>
    /// <returns>Набор актуальных объектов из кэша</returns>
    public async Task<IEnumerable<TCacheObject?>> GetAllActualAsync()
    {
        try
        {
            var allRedisKeys = await _distributedCacheHandler.GetAllKeysByPattern($"{_redisPrefix}.{_redisKeyField}*");
            return await GetByKeysAsync(allRedisKeys.Select(x => x.ToString()).ToArray(), _redisKeyField, false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Caching. Произошла ошибка в методе {nameof(GetAllActualAsync)}");
            throw;
        }
    }

    private string GetRedisKey(string? fieldName, string fieldValue) => $"{_redisPrefix}.{fieldName}.{fieldValue}";

    private async Task<CacheIndexObject?> GetRedis(string? fieldName, string fieldValue)
    {
        var redisKey = GetRedisKey(fieldName, fieldValue);
        
        return await GetRedis(redisKey);
    }

    private async Task<CacheIndexObject?> GetRedis(string key)
    {
        var json = await _distributedCacheHandler.GetAsync(key);
        return json.HasValue ? JsonToObject<CacheIndexObject>(json!) : null;
    }

    private async Task<CacheIndexObject[]> GetRedis(string? fieldName, string[] keys, bool needToFormatKeys)
    {
        await _lockGetRedisValues.WaitAsync(_criticalDelayToAvoidDeadLock);
        try
        {
            var prefixRedis = $"{_redisPrefix}.{fieldName}";
            var redisKeys = needToFormatKeys
                ? keys.Select(key => (RedisKey)$"{prefixRedis}.{key}").ToArray()
                : keys.Select(key => (RedisKey)key).ToArray();
            var redisValues = await _distributedCacheHandler.GetAsync(redisKeys);
            var json = redisValues
                .Where(x => x.HasValue)
                .Select(redisValue => JsonToObject<CacheIndexObject>(redisValue)).ToArray();
        
            return json;
        }
        finally
        {
            _lockGetRedisValues.Release();
        }
    }

    private List<KeyValuePair<RedisKey, RedisValue>> GetIndexWithData(TCacheObject? cacheObject, string cacheIndex)
    {
        var indexWithData =
            _indexProperties.Select(x =>
            {
                var indexAttribute = x.Value.GetCustomAttribute<CacheIndexAttribute>();
                var key = indexAttribute != null && indexAttribute.ToLower 
                    ? $"{_redisPrefix}.{x.Key}.{x.Value.GetValue(cacheObject)!.ToString().ToLower()}"
                    : $"{_redisPrefix}.{x.Key}.{x.Value.GetValue(cacheObject)!.ToString()}";
                
                return new KeyValuePair<RedisKey, RedisValue>(key, cacheIndex
                );
            }).ToList();

        return indexWithData;
    }

    private async Task<TCacheObject?> ReloadObjectInMemory(string key, string version)
    {
        if (_memory.TryGetValue(key, out var value) && value.Version == version)
            return value;
        
        TCacheObject? newObject;
        
        await _lockReloadInMemory.WaitAsync(_criticalDelayToAvoidDeadLock);
        try
        {
            if (_memory.TryGetValue(key, out var memoryValue) && memoryValue.Version == version)
                return memoryValue;
            
            newObject = await _cacheDataProvider.GetDataAsync(key);
            if (newObject == null) return default;
        
            _memory.AddOrUpdate(key, newObject, (_, _) => newObject);
        }
        finally
        {
            _lockReloadInMemory.Release();
        } 

        return newObject;
    }

    private async Task<TCacheObject?> ReloadObject(string key, string? version)
    {
        if (_memory.TryGetValue(key, out var value) && value.Version == version)
            return value;
        
        TCacheObject? newObject;
        
        await _lockReloadObject.WaitAsync(_criticalDelayToAvoidDeadLock);
        try
        {
            if (_memory.TryGetValue(key, out var memoryValue) && memoryValue.Version == version)
                return memoryValue;
            
            newObject = await _cacheDataProvider.GetDataAsync(key);
            if (newObject == null) return default;

            var indexCache = new CacheIndexObject
            {
                Id = key,
                Version = newObject.Version,
            };

            var jsonIndexCache = ObjectToJson(indexCache);
            var indexWithData = GetIndexWithData(newObject, jsonIndexCache);

            indexWithData.Add(new(GetRedisKey(_redisKeyField, key), jsonIndexCache));

            _memory.AddOrUpdate(key, newObject, (_, _) => newObject);

            await _distributedCacheHandler.SetAsync(indexWithData.ToArray());
        }
        finally
        {
            _lockReloadObject.Release();
        }

        return newObject;
    }

    private async Task LoadToCache(IEnumerable<TCacheObject> loadedObjects, bool isLoadAll = false)
    {
        var indexCaches = loadedObjects
            .ToDictionary(key => key.Id, value => value);
            
        if (!isLoadAll && indexCaches.All(x => _memory.TryGetValue(x.Key, out var value) && value.Version == x.Value.Version))
            return;
        
        var indexWithData = new List<KeyValuePair<RedisKey, RedisValue>>();

        foreach (var obj in loadedObjects)
        {
            if (!indexCaches.TryGetValue(obj.Id, out var val))
                continue;
                
            var cacheIndex = ObjectToJson(new CacheIndexObject
            {
                Id = val.Id,
                Version = val.Version
            });
            indexWithData.AddRange(GetIndexWithData(obj, cacheIndex));
            indexWithData.Add(new(GetRedisKey(_redisKeyField, obj.Id), cacheIndex));
            _memory.AddOrUpdate(obj.Id, obj, (_, _) => obj);
        }

        // Если загружаем все объекты - обновляем время
        if (isLoadAll)
            indexWithData.Add(new(_redisReloadAllTimeKey, DateTimeOffset.UtcNow.ToString()));

        await _distributedCacheHandler.SetAsync(indexWithData.ToArray());
    }

    private async Task<IEnumerable<TCacheObject?>> ReloadObject(string[] keys)
    {
        await _lockLoadAllActual.WaitAsync(_criticalDelayToAvoidDeadLock);
        try
        {
            var objects = await _cacheDataProvider.GetDataAsync(keys);
            if (!objects.Any())
                return Array.Empty<TCacheObject?>();

            await LoadToCache(objects);
            return objects;
        }
        finally
        {
            _lockLoadAllActual.Release();
        }
    }

    private ICacheObject? GetFromMemory(string key)
    {
        return _memory.TryGetValue(key, out var memoryValue) ? memoryValue : null;
    }
    
    private static string? GetKeyName() =>
        typeof(TCacheObject)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .First(prop => prop.IsDefined(typeof(CacheKeyAttribute))).Name;

    private static Dictionary<string, PropertyInfo> GetIndexedPropertiesWithNames() =>
        typeof(TCacheObject)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.IsDefined(typeof(CacheIndexAttribute)))
            .ToDictionary(prop => prop.Name);
    
    private static string ObjectToJson<T>(T cacheObject) => JsonConvert.SerializeObject(cacheObject);

    private static T JsonToObject<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    
    private async Task ReloadAllIfCacheNotFilled()
    {
        if (await IsRedisNeedReload()) await ReloadAllObjectAsync();
    }

    private async Task<bool> IsRedisNeedReload()
    {
        var reloadAllTime = await _distributedCacheHandler.GetAsync(_redisReloadAllTimeKey);
        
        return !reloadAllTime.HasValue;
    }

    private async Task<bool> IsLocalMemoryNeedReload()
    {
        var allRedisKeys = await _distributedCacheHandler.GetAllKeysByPattern($"{_redisPrefix}.{_redisKeyField}*");
        if (_memory.Count != allRedisKeys.Length) return true;
        
        var redisValues = await GetRedis(_redisKeyField, allRedisKeys.Select(x => x.ToString()).ToArray(), false);
        
        if (redisValues is not { Length: > 0 }) return true;

        return redisValues.Any(redisVal =>
            !_memory.TryGetValue(redisVal.Id, out var memVal) || memVal.Version != redisVal.Version);
    }
}