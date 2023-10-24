using StackExchange.Redis;

namespace Caching;

public interface IDistributedCacheHandler<TKey, TValue>
{
    /// <summary>
    /// Устанавливает значение по ключу в распределенное хранилище
    /// </summary>
    /// <param name="key">ключ</param>
    /// <param name="value">значение</param>
    Task SetAsync(TKey key, TValue value);
    
    /// <summary>
    /// Запись данных в распределенное хранилище
    /// </summary>
    /// <param name="data">Набор пар ключ-значение</param>
    Task SetAsync(KeyValuePair<TKey, TValue>[] data);

    /// <summary>
    /// Получение значения из распределенного хранилища
    /// </summary>
    /// <param name="key">Ключ</param>
    /// <returns>Значение по ключу</returns>
    Task<TValue?> GetAsync(TKey key);

    /// <summary>
    /// Получение набора данных из распределенного хранилища
    /// </summary>
    /// <param name="keys">Набор ключей</param>
    /// <returns>Набор значений по переданным ключам</returns>
    Task<TValue?[]> GetAsync(TKey[] keys);

    Task<RedisKey[]> GetAllKeysByPattern(TValue pattern);
}