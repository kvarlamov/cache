using System.Reflection;
using Microsoft.Extensions.Logging;
using Caching.Attributes;
using Caching.Tests.Entities;
using Caching.Tests.TestData;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using StackExchange.Redis;

namespace Caching.Tests;

public class UserLocalMemoryCacheTests
{
    private const string ReloadAllSuffix = "ReloadAll";
    
    private string _redisPrefix;
    private string _redisKeyField;
    private string _redisReloadAllTimeKey;
    private Dictionary<string, PropertyInfo> _indexProperties;
    private int _numberOfCachedFields;
    
    private InMemoryCacheHandler _distributedCacheHandler;
    private UserInMemoryDataProvider _cacheDataProvider;
    private LocalMemoryCache<User> _localMemoryCache;
    private int _totalDistributedCachedObjects;

    [OneTimeSetUp]
    public void SetupOnce()
    {
        _redisPrefix = nameof(User);
        var keys = GetKey();
        _redisKeyField = keys.First().Name;
        _redisReloadAllTimeKey = $"{_redisPrefix}.{ReloadAllSuffix}";
        _indexProperties = GetIndexedPropertiesWithNames();
        _numberOfCachedFields = keys.Length + _indexProperties.Count;
    }
    
    [SetUp]
    public void Setup()
    {
        _distributedCacheHandler = new InMemoryCacheHandler();
        _cacheDataProvider = new UserInMemoryDataProvider();
        _localMemoryCache = new(_distributedCacheHandler, _cacheDataProvider, Substitute.For<ILogger<LocalMemoryCache<User>>>());
        _totalDistributedCachedObjects = _cacheDataProvider.Users.Count(x => x.IsActualVersion) * _numberOfCachedFields;
    }

    #region ReloadAllObjectTests

    [Test]
    public async Task ReloadAllObjectTests()
    {
        // Arrange
        
        // в числе кэшируемых объектов в redis также должен быть ключ _redisReloadAllTimeKey
        _totalDistributedCachedObjects += 1;
        
        // Act
        await _localMemoryCache.ReloadAllObjectAsync();
        
        // Assert
        Assert.That(_distributedCacheHandler.DistributedStorage.Count, Is.EqualTo(expected: _totalDistributedCachedObjects));
        Assert.IsTrue(_distributedCacheHandler.DistributedStorage.ContainsKey(_redisReloadAllTimeKey));
        foreach (var user in await _cacheDataProvider.GetAllAsync())
        {
            var userKey = user.ImmutabilityId.ToString();
            var key = GetRedisKey(_redisKeyField, userKey);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(key, out var json));
            var cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);
            Assert.That(cachedObject.Id, Is.EqualTo(userKey));
            Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));
            
            foreach (var prop in _indexProperties)
            {
                var propertyValue = prop.Value.GetValue(user).ToString();
                if (prop.Value.GetCustomAttribute<CacheIndexAttribute>()?.ToLower == true)
                    propertyValue = propertyValue.ToLower();
                var index = GetRedisKey(prop.Key, propertyValue);
                Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out json));
                cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);
                
                Assert.That(cachedObject.Id, Is.EqualTo(userKey));
                Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));
            }
        }
    }

    #endregion
    
    #region GetByKey

    [Test, Description("Получение по ключу, объект не найден в redis и БД, возвращаем null")]
    public async Task GetByKey_UserNotExist_ReturnNull()
    {
        // Act
        var res = await _localMemoryCache.GetByKeyAsync(Guid.NewGuid().ToString());

        // Assert
        Assert.IsNull(res);
    }
    
    [Test, Description("Получение по ключу, объект не найден в redis, получаем из БД, возвращается объет")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task GetByKey_NotExistInRedisAndGetFromDb_ReturnUser(bool isDeleted)
    {
        var expected = _cacheDataProvider.Users.First(x => x.IsActualVersion);
        var userKey = expected.ImmutabilityId.ToString();
        _distributedCacheHandler.DistributedStorage.Clear();
        
        // Act
        var res = await _localMemoryCache.GetByKeyAsync(userKey);

        // Assert
        Assert.IsNotNull(res);
        Assert.That(res.ImmutabilityId, Is.EqualTo(expected.ImmutabilityId));
        var key = GetRedisKey(_redisKeyField, userKey);
        Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(key, out var value));
        var cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(value);
        Assert.That(cachedObj.Id, Is.EqualTo(userKey));
        Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        
        foreach (var prop in _indexProperties)
        {
            var propertyValue = prop.Value.GetValue(res).ToString();
            var index = GetRedisKey(prop.Key, propertyValue);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out var json));
            cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(json);
                
            Assert.That(cachedObj.Id, Is.EqualTo(userKey));
            Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        }
    }

    [Test, Description("Получение по ключу, объект найден в redis, версия совпадает, возвращается объет")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task GetByKey_EverythingFound_ReturnObject(bool isDeleted)
    {
        // Arrange
        await _localMemoryCache.ReloadAllObjectAsync();
        var expected = _cacheDataProvider.Users.First(x => x.IsActualVersion);
        var userKey = expected.ImmutabilityId.ToString();
        var redisKey = GetRedisKey(_redisKeyField, userKey);
        
        // Act
        var res = await _localMemoryCache.GetByKeyAsync(userKey);

        // Assert
        Assert.IsNotNull(res);
        Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(redisKey, out var value));
        var cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(value);
        Assert.That(cachedObj.Id, Is.EqualTo(userKey));
        Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        
        foreach (var prop in _indexProperties)
        {
            var propertyValue = prop.Value.GetValue(res).ToString();
            var index = GetRedisKey(prop.Key, propertyValue);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out var json));
            cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(json);
                
            Assert.That(cachedObj.Id, Is.EqualTo(userKey));
            Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        }
    }

    #endregion

    #region GetByKeys
    
    [Test]
    public async Task GetByKeys_NotFoundNothing_ReturnNull()
    {
        // Arrange
        var keys = (await _cacheDataProvider.GetAllAsync()).Select(x => x.ImmutabilityId.ToString()).ToArray();
        await _distributedCacheHandler.SetAsync(_redisReloadAllTimeKey, DateTime.UtcNow.ToString());

        // Act
        var res = await _localMemoryCache.GetByKeysAsync(keys);
        
        // Assert
        Assert.IsFalse(res.Any());
    }

    [Test, Description("Шаблоны только в бд, возвращается список шаблонов, кэш заполнен")]
    public async Task GetByKeys_InMemoryEmpty_ReturnList()
    {
        // Arrange
        var keys = (await _cacheDataProvider.GetAllAsync()).Select(x => x.ImmutabilityId.ToString()).ToArray();
        List<KeyValuePair<RedisKey, RedisValue>> data = new();
        foreach (var user in await _cacheDataProvider.GetAllAsync())
        {
            var userKey = user.ImmutabilityId.ToString();
            var key = GetRedisKey(_redisKeyField, userKey);
            var value = new CacheIndexObject
            {
                Id = userKey,
                Version = user.Version.ToString(),
            };
            data.Add(new KeyValuePair<RedisKey, RedisValue>(key, JsonConvert.SerializeObject(value)));
            foreach (var prop in _indexProperties)
            {
                var propertyValue = prop.Value.GetValue(user).ToString();
                var index = GetRedisKey(prop.Key, propertyValue);
                data.Add(new KeyValuePair<RedisKey, RedisValue>(index, JsonConvert.SerializeObject(value)));
            }
        }

        await _distributedCacheHandler.SetAsync(data.ToArray());

        // Act
        var res = await _localMemoryCache.GetByKeysAsync(keys);

        // Assert
        foreach (var user in await _cacheDataProvider.GetAllAsync())
        {
            var userKey = user.ImmutabilityId.ToString();
            var key = GetRedisKey(_redisKeyField, userKey);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(key, out var json));
            var cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);
            Assert.That(cachedObject.Id, Is.EqualTo(userKey));
            Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));

            foreach (var prop in _indexProperties)
            {
                var propertyValue = prop.Value.GetValue(user).ToString();
                var index = GetRedisKey(prop.Key, propertyValue);
                Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out json));
                cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);

                Assert.That(cachedObject.Id, Is.EqualTo(userKey));
                Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));
            }
        }
    }

    [Test, Description("Шаблоны есть в redis и в memorycache, возвращается список шаблонов")]
    public async Task GetByKeys_AllExistsInMemory_ReturnList()
    {
        // Arrange
        await _localMemoryCache.ReloadAllObjectAsync();
        var keys = (await _cacheDataProvider.GetAllAsync()).Select(x => x.ImmutabilityId.ToString()).ToArray();
        
        // Act
        var res = await _localMemoryCache.GetByKeysAsync(keys);

        // Assert
        foreach (var user in await _cacheDataProvider.GetAllAsync())
        {
            var userKey = user.ImmutabilityId.ToString();
            var key = GetRedisKey(_redisKeyField, userKey);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(key, out var json));
            var cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);
            Assert.That(cachedObject.Id, Is.EqualTo(userKey));
            Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));
            
            foreach (var prop in _indexProperties)
            {
                var propertyValue = prop.Value.GetValue(user).ToString();
                var index = GetRedisKey(prop.Key, propertyValue);
                Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out json));
                cachedObject = JsonConvert.DeserializeObject<CacheIndexObject>(json);
                
                Assert.That(cachedObject.Id, Is.EqualTo(userKey));
                Assert.That(cachedObject.Version, Is.EqualTo(user.Version.ToString()));
            }
        }
    }

    #endregion

    #region GetByIndex

    [Test, Description("Получение по ключу, объект не найден в redis и БД, возвращаем null")]
    [TestCase(nameof(User.Id),"100")]
    [TestCase(nameof(User.FullName),"Test100")]
    public async Task GetByIndex_UserNotExist_ReturnNull(string? fieldName, string fieldValue)
    {
        // Act 
        var result = await _localMemoryCache.GetByIndexAsync(fieldName, fieldValue);

        //Assert
        Assert.IsNull(result);
    }

    [Test, Description("Получение по ключу, объект найден в redis, версия совпадает, возвращается объет")]
    [TestCase(false, nameof(User.Id),"1")]
    [TestCase(false, nameof(User.FullName),"Test1")]
    [TestCase(true, nameof(User.Id),"5")]
    [TestCase(true, nameof(User.FullName),"Test5")]
    public async Task GetByIndex_EverythingFound_ReturnObject(bool isDeleted, string? fieldName, string fieldValue)
    {
        // Arrange
        await _localMemoryCache.ReloadAllObjectAsync();
        IEnumerable<User> query = fieldName == nameof(User.Id) 
            ? _cacheDataProvider.Users.Where(x => x.Id == long.Parse(fieldValue)) 
            : _cacheDataProvider.Users.Where(x => x.FullName == fieldValue);
        var expected = query.First(x => x.IsActualVersion);
        var userKey = expected.ImmutabilityId.ToString();
        var redisKey = GetRedisKey(_redisKeyField, userKey);
        
        // Act
        var res = await _localMemoryCache.GetByIndexAsync(fieldName, fieldValue);

        // Assert
        Assert.IsNotNull(res);
        Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(redisKey, out var value));
        var cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(value);
        Assert.That(cachedObj.Id, Is.EqualTo(userKey));
        Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        
        foreach (var prop in _indexProperties)
        {
            var propertyValue = prop.Value.GetValue(res).ToString();
            var index = GetRedisKey(prop.Key, propertyValue);
            Assert.IsTrue(_distributedCacheHandler.DistributedStorage.TryGetValue(index, out var json));
            cachedObj = JsonConvert.DeserializeObject<CacheIndexObject>(json);
                
            Assert.That(cachedObj.Id, Is.EqualTo(userKey));
            Assert.That(cachedObj.Version, Is.EqualTo(expected.Version.ToString()));
        }
    }

    #endregion

    #region PrivateMethods

    private static PropertyInfo[] GetKey() =>
        typeof(User)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.IsDefined(typeof(CacheKeyAttribute))).ToArray();
    
    private static Dictionary<string, PropertyInfo> GetIndexedPropertiesWithNames() =>
        typeof(User)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.IsDefined(typeof(CacheIndexAttribute)))
            .ToDictionary(prop => prop.Name);
    
    private string GetRedisKey(string fieldName, string fieldValue) => $"{_redisPrefix}.{fieldName}.{fieldValue}";

    #endregion
}