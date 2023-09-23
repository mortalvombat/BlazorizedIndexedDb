using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Blazorized.IndexedDb;

public class BlazorizedDbFactory : IBlazorizedDbFactory
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<string, IndexedDbManager> _dbs = new Dictionary<string, IndexedDbManager>();
    //private IJSObjectReference _module;

    public BlazorizedDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime)
    {
        _serviceProvider = serviceProvider;
        _jsRuntime = jSRuntime;
    }

    //public async Task<IndexedDbManager> CreateAsync(DbStore dbStore)
    //{
    //    var manager = new IndexedDbManager(dbStore, _jsRuntime);
    //    var importedManager = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Blazorized.IndexedDb/magicDB.js");
    //    return manager;
    //}

    public async Task<IndexedDbManager> GetDbManager(string dbName)
    {
        if (!_dbs.Any())
            await BuildFromServices();
        if (_dbs.ContainsKey(dbName))
            return _dbs[dbName];

#pragma warning disable CS8603 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        return null;
#pragma warning restore CS8603 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }

    public Task<IndexedDbManager> GetDbManager(DbStore dbStore)
        => GetDbManager(dbStore.Name);

    private async Task BuildFromServices()
    {
        var dbStores = _serviceProvider.GetServices<DbStore>();
        if (dbStores != null)
        {
            foreach (var dbStore in dbStores)
            {
                Console.WriteLine($"{dbStore.Name}{dbStore.Version}{dbStore.StoreSchemas.Count}");
                var db = new IndexedDbManager(dbStore, _jsRuntime);
                await db.OpenDb();
                _dbs.Add(dbStore.Name, db);
            }
        }
    }
}