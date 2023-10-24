using Caching.Attributes;

namespace Caching;

public interface ILocalMemoryCache<TCacheObject> where TCacheObject : class, ICacheObject
{
    /// <summary>
    /// Загружает все объекты в кэш и устанавливает в Redis значение ключа _redisReloadAllTimeKey
    /// </summary>
    Task ReloadAllObjectAsync();

    /// <summary>
    /// Обновление объекта в кэше по ключу, определяемому атрибутом <see cref="CacheKeyAttribute"/>
    /// </summary>
    Task ReloadObjectAsync(string key);

    /// <summary>
    /// Получает объект по ключу, определяемому атрибутом <see cref="CacheKeyAttribute"/>
    /// </summary>
    Task<TCacheObject?> GetByKeyAsync(string key);

    /// <summary>
    /// Получить по набору ключей
    /// </summary>
    Task<List<TCacheObject?>> GetByKeysAsync(string[] keys, string? fieldName = null, bool needToFormatKeys = true);

    /// <summary>
    /// Получает набор объектов по значениям поля, определяемому атрибутом <see cref="CacheIndexAttribute"/>
    /// </summary>
    Task<List<TCacheObject?>> GetByIndexesAsync(string fieldName, string[] keys);

    /// <summary>
    /// Получает кэшируемый объект по полю, определяемому атрибутом <see cref="CacheIndexAttribute"/>
    /// </summary>
    Task<TCacheObject?> GetByIndexAsync(string? fieldName, string fieldValue);

    /// <summary>
    /// Получить все актуальные объекты из кэша
    /// </summary>
    /// <returns></returns>
    Task<IEnumerable<TCacheObject?>> GetAllActualAsync();
}