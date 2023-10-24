using Caching.Attributes;

namespace Caching;

/// <summary>
/// Интерфейс провайдера данных для сервиса кэширования
/// </summary>
/// <typeparam name="TCacheObject"></typeparam>
public interface ICacheDataProvider<TCacheObject> where TCacheObject : class, ICacheObject
{
    /// <summary>
    /// Получает объект из источника данных по ключу
    /// </summary>
    /// <param name="key">ключ, определяемый атрибутом <see cref="CacheKeyAttribute"/></param>
    /// <returns>объект из источника данных</returns>
    public Task<TCacheObject?> GetDataAsync(string key);
    
    /// <summary>
    /// Получает набор объектов по набору ключей
    /// </summary>
    /// <param name="keys">набор ключей, определяемых атрибутом <see cref="CacheKeyAttribute"/></param>
    /// <returns>Набор объектов из источника данных</returns>
    public Task<IEnumerable<TCacheObject>> GetDataAsync(string[] keys);
    
    /// <summary>
    /// Получить все данные из источника данных
    /// </summary>
    /// <returns>Набор объектов из источника данных</returns>
    public Task<IEnumerable<TCacheObject>> GetAllAsync();
}