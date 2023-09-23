using Blazorized.IndexedDb.Helpers;

namespace Blazorized.IndexedDb;

public class BlazorizedTableAttribute : Attribute
{
    public string SchemaName { get; }
    public string DatabaseName { get; }

    public BlazorizedTableAttribute(string schemaName, string? databaseName = null)
    {
        SchemaName = schemaName;
        DatabaseName = string.IsNullOrWhiteSpace(databaseName) ? SchemaHelper.defaultNone : databaseName;
    }
}
