namespace Caching;

/// <summary>
/// Контракт кэшируемого объекта
/// </summary>
public interface ICacheObject
{
    /// <summary>
    /// Идентификатор кэшируемого объекта
    /// </summary>
    public string Id { get; }
    
    /// <summary>
    /// Версия кэшируемого объекта
    /// </summary>
    public string Version { get; }
}