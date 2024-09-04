using Blazorized.IndexedDb.SchemaAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Blazorized.IndexedDb.Helpers
{
    public static class SchemaHelper
    {
        public const string defaultNone = "DefaultedNone";

        public static string GetSchemaName<T>() where T : class
        {
            Type type = typeof(T);
            string schemaName;
            var schemaAttribute = type.GetCustomAttribute<BlazorizedTableAttribute>();
            if (schemaAttribute != null)
                schemaName = schemaAttribute.SchemaName;
            else
                schemaName = type.Name;
            return schemaName;
        }

        public static List<string> GetPropertyNamesFromExpression<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            var propertyNames = new List<string>();

            if (predicate.Body is BinaryExpression binaryExpression)
            {
                if (binaryExpression.Left is MemberExpression left) propertyNames.Add(left.Member.Name);
                if (binaryExpression.Right is MemberExpression right) propertyNames.Add(right.Member.Name);
            }
            else if (predicate.Body is MethodCallExpression methodCallExpression)
                if (methodCallExpression.Object is MemberExpression argument) propertyNames.Add(argument.Member.Name);
            return propertyNames;
        }


        public static List<StoreSchema> GetAllSchemas(string? databaseName = null)
        {
            List<StoreSchema> schemas = [];
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    var schemaAttribute = type.GetCustomAttribute<BlazorizedTableAttribute>();
                    if (schemaAttribute != null)
                    {
                        string DbName = string.IsNullOrWhiteSpace(databaseName) ? defaultNone : databaseName;
                        if (schemaAttribute.DatabaseName.Equals(DbName))
                            schemas.Add(GetStoreSchema(type));
                    }
                }
            }
            return schemas;
        }

        public static StoreSchema GetStoreSchema<T>(string? name = null, string? primaryKey = null) where T : class
        {
            return GetStoreSchema(typeof(T), name, primaryKey);
        }

        public static StoreSchema GetStoreSchema(Type type, string? name = null, string? primaryKey = null)
        {
            var schema = new StoreSchema();

            if (string.IsNullOrWhiteSpace(name))
            {
                // Get the schema name from the SchemaAnnotationDbAttribute if it exists
                var schemaAttribute = type.GetCustomAttribute<BlazorizedTableAttribute>();
                schema.Name = schemaAttribute?.SchemaName ?? type.Name;
            }
            else
            {
                schema.Name = name;
            }

            // Get the primary key property
            if (primaryKey != null)
            {
                var property = type.GetProperty(primaryKey) ?? throw new InvalidOperationException($"The entity does not have a primare key property with the name '{primaryKey}'");
                schema.PrimaryKey = primaryKey;
            }
            else
            {
                //TODO: type.GetPropertyied should be cvalled once, its expensive
                PropertyInfo? primaryKeyProperty = type.GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute)))
                    ?? throw new InvalidOperationException("The entity does not have a primary key attribute and no primary key is specified");

                if (type.GetProperties().Count(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute))) > 1)
                    throw new InvalidOperationException("The entity has more than one primary key attribute.");

                schema.PrimaryKey = primaryKeyProperty.GetPropertyColumnName<BlazorizedPrimaryKeyAttribute>();
            }

            // Get the unique index properties
            var uniqueIndexProperties = type.GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(BlazorizedUniqueIndexAttribute)));

            foreach (PropertyInfo? prop in uniqueIndexProperties)
                if (prop != null)
                    schema.UniqueIndexes.Add(prop.GetPropertyColumnName<BlazorizedUniqueIndexAttribute>());

            // Get the index properties
            var indexProperties = type.GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(BlazorizedIndexAttribute)));

            foreach (var prop in indexProperties)
                schema.Indexes.Add(prop.GetPropertyColumnName<BlazorizedIndexAttribute>());

            return schema;
        }

        public static string GetPropertyColumnName(this PropertyInfo prop, Type attributeType)
        {
            Attribute? attribute = Attribute.GetCustomAttribute(prop, attributeType);

            string columnName = prop.Name;

            if (attribute != null)
            {
                PropertyInfo[] properties = attributeType.GetProperties();
                foreach (PropertyInfo property in properties)
                {
                    if (Attribute.IsDefined(property, typeof(BlazorizedColumnNameDesignatorAttribute)))
                    {
                        object? designatedColumnNameObject = property.GetValue(attribute);
                        if (designatedColumnNameObject != null)
                        {
                            string designatedColumnName = designatedColumnNameObject as string ?? designatedColumnNameObject.ToString();
                            if (!string.IsNullOrWhiteSpace(designatedColumnName))
                                columnName = designatedColumnName;
                        }
                        break;
                    }
                }
            }

            return columnName;
        }
        public static string GetPropertyColumnName<T>(this PropertyInfo prop) where T : Attribute
        {
            //T? attribute = (T?)Attribute.GetCustomAttribute(prop, typeof(T));
            return prop.GetPropertyColumnName(typeof(T));
        }


        //public StoreSchema GetStoreSchema<T>(bool PrimaryKeyAuto = true) where T : class
        //{
        //    StoreSchema schema = new StoreSchema();
        //    schema.PrimaryKeyAuto = PrimaryKeyAuto;


        //    return schema;
        //}
    }
}
