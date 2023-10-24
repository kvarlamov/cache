using System.Reflection;
using Caching.Attributes;
using Caching.Tests.Entities;

namespace Caching.Tests.TestData;

public class UserInMemoryDataProvider : ICacheDataProvider<User>
{
    public List<User> Users { get; set; }

    public UserInMemoryDataProvider()
    {
        Users = GetUsers();
    }

    public Task<User?> GetDataAsync(string key)
    {
        if (!Guid.TryParse(key, out var immutabilityId))
            throw new ArgumentException($"{key} не является Guid");

        return Task.FromResult(Users.FirstOrDefault(t => t.ImmutabilityId == immutabilityId));
    }

    public Task<IEnumerable<User>> GetDataAsync(string[] keys) => Task.FromResult(Users.Where(t => keys.Contains(t.ImmutabilityId.ToString())));

    public Task<IEnumerable<User>> GetAllAsync() => Task.FromResult(Users.Where(x => x.IsActualVersion));

    public IEnumerable<User> GetNotCachedObject(string[] keyInCache)
    {
        throw new NotImplementedException();
    }
    
    private List<User> GetUsers()
    {
        return new List<User>()
        {
            new()
            {
                Id = 1,
                ImmutabilityId = Guid.NewGuid(),
                FullName = "Ivanov Alex"
            },
            new()
            {
                Id = 2,
                ImmutabilityId = Guid.NewGuid(),
                FullName = "Petrov Ivan"
            }
        };
    }
    
    private static PropertyInfo GetKeyProperty() =>
        typeof(User)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .First(prop => prop.IsDefined(typeof(CacheKeyAttribute)));
}