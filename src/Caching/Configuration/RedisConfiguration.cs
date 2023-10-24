namespace Caching.Configuration;

internal class RedisConfiguration
{
    internal const string ConfigurationSectionName = "Redis";
    
    /// <summary>
    /// Точки подключения к Redis redis://&lt;server-name&gt;:&lt;port&gt; 
    /// </summary>
    public string[]? EndPoints { get; set; }

    /// <summary>
    /// Использовать ли SSL при подключении к Redis (по умолчанию false)
    /// </summary>
    public bool? Ssl { get; set; }
    
    /// <summary>
    /// Имя пользователя для подключения к Redis (для анонимного подключения не указывается)
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Пароль для подключения к Redis (для анонимного подключения не указывается)
    /// </summary>
    public string? Password { get; set; }
}