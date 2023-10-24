using Caching.Attributes;

namespace Caching.Tests.Entities;

public class User : ICacheObject
{
    [CacheIndex]
    public long Id { get; set; }
    
    [CacheKey]
    public Guid ImmutabilityId { get; set; }

    [CacheIndex(toLower: true)]
    public string FullName { get; set; }
    public bool IsActualVersion { get; set; }

    public long Version { get; set; }

    string ICacheObject.Id { get => ImmutabilityId.ToString(); }
    string ICacheObject.Version { get => Version.ToString(); }
}