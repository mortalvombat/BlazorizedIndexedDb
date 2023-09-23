namespace Blazorized.IndexedDb;

public interface IBlazorizedDbFactory
{
    Task<IndexedDbManager> GetDbManager(string dbName);

    Task<IndexedDbManager> GetDbManager(DbStore dbStore);
}