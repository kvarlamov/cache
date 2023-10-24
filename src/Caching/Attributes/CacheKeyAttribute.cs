namespace Caching.Attributes;

/// <summary>
/// Атрибут, определяющий ключ, по которому кэшируется объект
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class CacheKeyAttribute : Attribute
{
}