using Marten;
using Caching;
using Caching.Tests.Entities;
using Newtonsoft.Json;

namespace BenchmarkLocalMemoryCache;

public class MartenCacheDataProvider : ICacheDataProvider<User>
{
    private readonly IDocumentStore _store;
    private static string ConnectionString => "somestring";

    public MartenCacheDataProvider()
    {
        _store = DocumentStore.For(options =>
        {
            options.Connection(ConnectionString);
            options.Schema.For<User>()
                .Duplicate(x => x.Id)
                .Duplicate(x => x.Version, configure: idx => idx.SortOrder = Weasel.Postgresql.Tables.SortOrder.Desc)
                .Duplicate(x => x.ImmutabilityId)
                .Index(x => x.FullName)
                .Duplicate(x => x.IsActualVersion);

            var serializer = new Marten.Services.JsonNetSerializer();
            serializer.Customize(x => x.TypeNameHandling = TypeNameHandling.None);
            options.Serializer(serializer);
        });
    }

    public async Task<User> GetDataAsync(string key)
    {
        await using var session = _store.QuerySession();
        if (!Guid.TryParse(key, out var immutabilityId))
            throw new ArgumentException($"{key} не является Guid");

        var user = await session.Query<User>()
            .FirstOrDefaultAsync(x => x.ImmutabilityId == immutabilityId && x.IsActualVersion);

        return user;
    }

    public async Task<IEnumerable<User>> GetDataAsync(string[] keys)
    {
        var keysToFind = keys.Select(x => Guid.Parse(x)).ToArray();

        await using var session = _store.QuerySession();

        var users = await session.Query<User>()
            .Where(x => x.ImmutabilityId.In(keysToFind) && x.IsActualVersion).ToListAsync();

        return users;
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        await using var session = _store.QuerySession();
        var users = await session.Query<User>()
            .Where(t => t.IsActualVersion)
            .ToListAsync();

        return users;
    }
}