namespace Blazorized.IndexedDb
{
    public class UpdateRecord<T> : StoreRecord<T>
    {
        public object Key { get; set; }
    }
}