using Blazorized.IndexedDb;
using Blazorized.IndexedDb.SchemaAnnotations;

namespace IndexDb.Example
{
    [BlazorizedTable("Person", DbNames.Client)]
    public class Person
    {
        //[BlazorizedPrimaryKey("id")]
        public int id { get; set; }

        [BlazorizedNotMapped]
        public int _Id => id;

        [BlazorizedIndex]
        public string Name { get; set; }

        [BlazorizedIndex("Age")]
        public int _Age { get; set; }

        [BlazorizedIndex]
        public int TestInt { get; set; }

        [BlazorizedUniqueIndex("guid")]
        public Guid GUIY { get; set; } = Guid.NewGuid();

        [BlazorizedEncrypt]
        public string Secret { get; set; }

        [BlazorizedNotMapped]
        public string DoNotMapTest { get; set; }

        [BlazorizedNotMapped]
        public string SecretDecrypted { get; set; }

        private bool testPrivate { get; set; } = false;

        public bool GetTest()
        {
            return true;
        }

    }

    
}
