namespace Caching;

/// <summary>
/// Кэшируемый объект
/// </summary>
public class CacheIndexObject : ICacheObject
{
    /// <inheritdoc />
    public string Id { get; set; }

    /// <inheritdoc />
    public string Version { get; set; }
}