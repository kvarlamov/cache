namespace Caching.Attributes;

/// <summary>
/// Атрибут, определяющий поле, по которому кэшируется объект
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
public class CacheIndexAttribute : Attribute
{
    /// <summary>
    /// Признак - необходимо ли привести значение свойства к нижнегу регистру
    /// </summary>
    public bool ToLower { get; set; }

    public CacheIndexAttribute(bool toLower = false)
    {
        ToLower = toLower;
    }
}