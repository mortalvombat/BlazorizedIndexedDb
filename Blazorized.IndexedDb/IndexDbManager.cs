using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Blazorized.IndexedDb.Helpers;
using Blazorized.IndexedDb.Models;
using Blazorized.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Blazorized.IndexedDb;

/// <summary>
/// Provides functionality for accessing IndexedDB from Blazor application
/// </summary>
public class IndexedDbManager
{
    readonly DbStore _dbStore;
    readonly IJSRuntime _jsRuntime;
    const string InteropPrefix = "window.magicBlazorDB";
    readonly DotNetObjectReference<IndexedDbManager> _objReference;
    readonly Dictionary<Guid, WeakReference<Action<BlazorDbEvent>>> _transactions = new();
    readonly Dictionary<Guid, TaskCompletionSource<BlazorDbEvent>> _taskTransactions = new();

    private IJSObjectReference? Module { get; set; }
    /// <summary>
    /// A notification event that is raised when an action is completed
    /// </summary>
    public event EventHandler<BlazorDbEvent> ActionCompleted;

    /// <summary>
    /// Ctor
    /// </summary>
    /// <param name="dbStore"></param>
    /// <param name="jsRuntime"></param>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    internal IndexedDbManager(DbStore dbStore, IJSRuntime jsRuntime)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        _objReference = DotNetObjectReference.Create(this);
        _dbStore = dbStore;
        _jsRuntime = jsRuntime;
    }

    public async Task<IJSObjectReference> GetModule(IJSRuntime jsRuntime)
    {
        Module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Blazorized.IndexedDb/magicDB.js");
        return Module;
    }

    public List<StoreSchema> Stores => _dbStore.StoreSchemas;
    public string CurrentVersion => _dbStore.Version;
    public string DbName => _dbStore.Name;

    /// <summary>
    /// Opens the IndexedDB defined in the DbStore. Under the covers will create the database if it does not exist
    /// and create the stores defined in DbStore.
    /// </summary>
    /// <returns></returns>
    public async Task<Guid> OpenDb(Action<BlazorDbEvent>? action = null)
    {
        var trans = GenerateTransaction(action);
        await CallJavascriptVoid(IndexedDbFunctions.CREATE_DB, trans, _dbStore);
        return trans;
    }

    /// <summary>
    /// Deletes the database corresponding to the dbName passed in
    /// </summary>
    /// <param name="dbName">The name of database to delete</param>
    /// <returns></returns>
    public async Task<Guid> DeleteDb(string dbName, Action<BlazorDbEvent>? action = null)
    {
        if (string.IsNullOrEmpty(dbName))
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        var trans = GenerateTransaction(action);
        await CallJavascriptVoid(IndexedDbFunctions.DELETE_DB, trans, dbName);
        return trans;
    }


    /// <summary>
    /// Deletes the database corresponding to the dbName passed in
    /// Waits for response
    /// </summary>
    /// <param name="dbName">The name of database to delete</param>
    /// <returns></returns>
    public async Task<BlazorDbEvent> DeleteDbAsync(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        var trans = GenerateTransaction();
        await CallJavascriptVoid(IndexedDbFunctions.DELETE_DB, trans.trans, dbName);
        return await trans.task;
    }

    /// <summary>
    /// Adds a new record/obj to the specified store
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="recordToAdd">An instance of StoreRecord that provides the store name and the data to add</param>
    /// <returns></returns>
    private async Task<Guid> AddRecord<T>(StoreRecord<T> recordToAdd, Action<BlazorDbEvent>? action = null)
    {
        var trans = GenerateTransaction(action);
        try
        {
            recordToAdd.DbName = DbName;
            await CallJavascriptVoid(IndexedDbFunctions.ADD_ITEM, trans, recordToAdd);
        }
        catch (JSException e)
        {
            RaiseEvent(trans, true, e.Message);
        }
        return trans;
    }

    public async Task<Guid> Add<T>(T record, Action<BlazorDbEvent>? action = null) where T : class
    {
        string schemaName = SchemaHelper.GetSchemaName<T>();

        T? myClass = null;
        object? processedRecord = await ProcessRecord(record);
        if (processedRecord is ExpandoObject)
            myClass = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(processedRecord));
        else
            myClass = (T?)processedRecord;

        var trans = GenerateTransaction(action);
        try
        {
            Dictionary<string, object?>? convertedRecord = null;
            if (processedRecord is ExpandoObject obj)
            {
                var result = obj?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                if (result != null)
                    convertedRecord = result;
            }
            else
                convertedRecord = ManagerHelper.ConvertRecordToDictionary(myClass);
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

            // Convert the property names in the convertedRecord dictionary
            if (convertedRecord != null)
            {
                var updatedRecord = ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings);

                if (updatedRecord != null)
                {
                    StoreRecord<Dictionary<string, object?>> RecordToSend = new()
                    {
                        DbName = this.DbName,
                        StoreName = schemaName,
                        Record = updatedRecord
                    };

                    await CallJavascriptVoid(IndexedDbFunctions.ADD_ITEM, trans, RecordToSend);
                }
            }
        }
        catch (JSException e)
        {
            RaiseEvent(trans, true, e.Message);
        }
        return trans;
    }

    public async Task<string> Decrypt(string EncryptedValue)
    {
        EncryptionFactory encryptionFactory = new(_jsRuntime, this);
        string decryptedValue = await encryptionFactory.Decrypt(EncryptedValue, _dbStore.EncryptionKey);
        return decryptedValue;
    }

    private async Task<object?> ProcessRecord<T>(T record) where T : class
    {
        string schemaName = SchemaHelper.GetSchemaName<T>();
        StoreSchema? storeSchema = Stores.FirstOrDefault(s => s.Name == schemaName) ?? throw new InvalidOperationException($"StoreSchema not found for '{schemaName}'");

        // Encrypt properties with EncryptDb attribute
        var propertiesToEncrypt = typeof(T).GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(BlazorizedEncryptAttribute), false).Length > 0);

        EncryptionFactory encryptionFactory = new(_jsRuntime, this);
        foreach (var property in propertiesToEncrypt)
        {
            if (property.PropertyType != typeof(string))
                throw new InvalidOperationException("EncryptDb attribute can only be used on string properties.");

            string? originalValue = property.GetValue(record) as string;
            if (!string.IsNullOrWhiteSpace(originalValue))
            {
                string encryptedValue = await encryptionFactory.Encrypt(originalValue, _dbStore.EncryptionKey);
                property.SetValue(record, encryptedValue);
            }
            else
                property.SetValue(record, originalValue);
        }

        // Proceed with adding the record
        if (storeSchema.PrimaryKeyAuto)
        {
            var primaryKeyProperty = typeof(T)
                .GetProperties()
                .FirstOrDefault(p => p.GetCustomAttributes(typeof(BlazorizedPrimaryKeyAttribute), false).Length > 0);

            if (primaryKeyProperty != null)
            {
                Dictionary<string, object?> recordAsDict;

                var primaryKeyValue = primaryKeyProperty.GetValue(record);
                if (primaryKeyValue == null || primaryKeyValue.Equals(GetDefaultValue(primaryKeyValue.GetType())))
                {
                    recordAsDict = typeof(T).GetProperties()
                    .Where(p => p.Name != primaryKeyProperty.Name && p.GetCustomAttributes(typeof(BlazorizedNotMappedAttribute), false).Length == 0)
                    .ToDictionary(p => p.Name, p => p.GetValue(record));
                }
                else
                {
                    recordAsDict = typeof(T).GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(BlazorizedNotMappedAttribute), false).Length == 0)
                    .ToDictionary(p => p.Name, p => p.GetValue(record));
                }

                // Create a new ExpandoObject and copy the key-value pairs from the dictionary
                var expandoRecord = new ExpandoObject() as IDictionary<string, object?>;
                foreach (var kvp in recordAsDict)
                    expandoRecord.Add(kvp);
                return expandoRecord as ExpandoObject;
            }
        }

        return record;
    }

    // Returns the default value for the given type
    private static object? GetDefaultValue(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// Adds records/objects to the specified store in bulk
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="recordsToBulkAdd">The data to add</param>
    /// <returns></returns>
    private async Task<Guid> BulkAddRecord<T>(string storeName, IEnumerable<T> recordsToBulkAdd, Action<BlazorDbEvent>? action = null)
    {
        var trans = GenerateTransaction(action);
        try
        {
            await CallJavascriptVoid(IndexedDbFunctions.BULKADD_ITEM, trans, DbName, storeName, recordsToBulkAdd);
        }
        catch (JSException e)
        {
            RaiseEvent(trans, true, e.Message);
        }
        return trans;
    }

    //public async Task<Guid> AddRange<T>(IEnumerable<T> records, Action<BlazorDbEvent> action = null) where T : class
    //{
    //    string schemaName = SchemaHelper.GetSchemaName<T>();
    //    var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

    //    List<obj> processedRecords = new List<obj>();
    //    foreach (var record in records)
    //    {
    //        obj processedRecord = await ProcessRecord(record);

    //        if (processedRecord is ExpandoObject)
    //        {
    //            var convertedRecord = ((ExpandoObject)processedRecord).ToDictionary(kv => kv.Key, kv => (obj)kv.Value);
    //            processedRecords.Add(ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings));
    //        }
    //        else
    //        {
    //            var convertedRecord = ManagerHelper.ConvertRecordToDictionary((T)processedRecord);
    //            processedRecords.Add(ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings));
    //        }
    //    }

    //    return await BulkAddRecord(schemaName, processedRecords, action);
    //}

    /// <summary>
    /// Adds records/objects to the specified store in bulk
    /// Waits for response
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="recordsToBulkAdd">An instance of StoreRecord that provides the store name and the data to add</param>
    /// <returns></returns>
    private async Task<BlazorDbEvent> BulkAddRecordAsync<T>(string storeName, IEnumerable<T> recordsToBulkAdd)
    {
        var trans = GenerateTransaction();
        try
        {
            await CallJavascriptVoid(IndexedDbFunctions.BULKADD_ITEM, trans.trans, DbName, storeName, recordsToBulkAdd);
        }
        catch (JSException e)
        {
            RaiseEvent(trans.trans, true, e.Message);
        }
        return await trans.task;
    }

    public async Task AddRange<T>(IEnumerable<T> records) where T : class
    {
        string schemaName = SchemaHelper.GetSchemaName<T>();

        //var trans = GenerateTransaction(null);
        //var TableCount = await CallJavascript<int>(IndexedDbFunctions.COUNT_TABLE, trans, DbName, schemaName);
        List<Dictionary<string, object?>> processedRecords = [];
        foreach (var record in records)
        {
            bool IsExpando = false;
            T? myClass = null;

            object? processedRecord = await ProcessRecord(record);
            if (processedRecord is ExpandoObject)
            {
                myClass = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(processedRecord));
                IsExpando = true;
            }
            else
                myClass = (T?)processedRecord;


            Dictionary<string, object?>? convertedRecord = null;
            if (processedRecord is ExpandoObject obj)
            {
                var result = obj?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                if (result != null)
                    convertedRecord = result;
            }
            else
                convertedRecord = ManagerHelper.ConvertRecordToDictionary(myClass);
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

            // Convert the property names in the convertedRecord dictionary
            if (convertedRecord != null)
            {
                var updatedRecord = ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings);

                if (updatedRecord != null)
                {
                    if (IsExpando)
                    {
                        //var test = updatedRecord.Cast<Dictionary<string, obj>();
                        var dictionary = updatedRecord as Dictionary<string, object?>;
                        processedRecords.Add(dictionary);
                    }
                    else
                        processedRecords.Add(updatedRecord);
                }
            }
        }

        await BulkAddRecordAsync(schemaName, processedRecords);
    }



    public async Task<Guid> Update<T>(T item, Action<BlazorDbEvent>? action = null) where T : class
    {
        var trans = GenerateTransaction(action);
        try
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute)));
            if (primaryKeyProperty != null)
            {
                object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);
                if (primaryKeyValue != null)
                {
                    UpdateRecord<Dictionary<string, object?>> record = new()
                    {
                        Key = primaryKeyValue,
                        DbName = this.DbName,
                        StoreName = schemaName,
                        Record = convertedRecord
                    };

                    // Get the primary key value of the item
                    await CallJavascriptVoid(IndexedDbFunctions.UPDATE_ITEM, trans, record);
                }
                else
                {
                    throw new ArgumentException("Item being updated must have a key.");
                }
            }
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }
        return trans;
    }

    public async Task<Guid> UpdateRange<T>(IEnumerable<T> items, Action<BlazorDbEvent>? action = null) where T : class
    {
        var trans = GenerateTransaction(action);
        try
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute)));

            if (primaryKeyProperty != null)
            {
                List<UpdateRecord<Dictionary<string, object?>>> recordsToUpdate = [];

                foreach (var item in items)
                {
                    object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                    var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);

                    if (primaryKeyValue != null)
                        recordsToUpdate.Add(new UpdateRecord<Dictionary<string, object?>>()
                        {
                            Key = primaryKeyValue,
                            DbName = this.DbName,
                            StoreName = schemaName,
                            Record = convertedRecord
                        });

                    await CallJavascriptVoid(IndexedDbFunctions.BULKADD_UPDATE, trans, recordsToUpdate);
                }
            }
            else
            {
                throw new ArgumentException("Item being update range item must have a key.");
            }
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }
        return trans;
    }

    public async Task<TResult?> GetById<TResult>(object key) where TResult : class
    {
        string schemaName = SchemaHelper.GetSchemaName<TResult>();

        // Find the primary key property
        var primaryKeyProperty = typeof(TResult)
            .GetProperties()
            .FirstOrDefault(p => p.GetCustomAttributes(typeof(BlazorizedPrimaryKeyAttribute), false).Length > 0) ?? throw new InvalidOperationException("No primary key property found with PrimaryKeyDbAttribute.");

        // Check if the key is of the correct type
        if (!primaryKeyProperty.PropertyType.IsInstanceOfType(key))
            throw new ArgumentException($"Invalid key type. Expected: {primaryKeyProperty.PropertyType}, received: {key.GetType()}");

        var trans = GenerateTransaction(null);

        string columnName = primaryKeyProperty.GetPropertyColumnName<BlazorizedPrimaryKeyAttribute>();

        var data = new { DbName, StoreName = schemaName, Key = columnName, KeyValue = key };

        try
        {
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<TResult>();
            var RecordToConvert = await CallJavascript<Dictionary<string, object>>(IndexedDbFunctions.FIND_ITEMV2, trans, data.DbName, data.StoreName, data.KeyValue);
            if (RecordToConvert != null)
                return ConvertIndexedDbRecordToCRecord<TResult>(RecordToConvert, propertyMappings);
            else
                return default;
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }

        return default;
    }

    public BlazorizedQuery<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class
    {
        string schemaName = SchemaHelper.GetSchemaName<T>();
        BlazorizedQuery<T> query = new(schemaName, this);
        Console.WriteLine("Inside Where");
        // Preprocess the predicate to break down Any and All expressions
        var preprocessedPredicate = PreprocessPredicate(predicate);
        _ = preprocessedPredicate.ToString();
        Console.WriteLine($"preprocessedPredicate = {preprocessedPredicate}");
        CollectBinaryExpressions(preprocessedPredicate.Body, preprocessedPredicate, query.JsonQueries);

        return query;
    }

    private static Expression<Func<T, bool>> PreprocessPredicate<T>(Expression<Func<T, bool>> predicate)
    {
        var visitor = new PredicateVisitor<T>();
        var newExpression = visitor.Visit(predicate.Body);

        return Expression.Lambda<Func<T, bool>>(newExpression, predicate.Parameters);
    }

    public async Task<IList<T>?> WhereV2<T>(string storeName, List<string> jsonQuery, BlazorizedQuery<T> query) where T : class
    {
        var trans = GenerateTransaction(null);

        try
        {
            string? jsonQueryAdditions = null;
            if (query != null && query.StoredBlazorizedQueries != null && query.StoredBlazorizedQueries.Count > 0)
                jsonQueryAdditions = JsonConvert.SerializeObject(query.StoredBlazorizedQueries.ToArray());
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();
            IList<Dictionary<string, object>>? ListToConvert =
                await CallJavascript<IList<Dictionary<string, object>>>
                (IndexedDbFunctions.WHEREV2, trans, DbName, storeName, jsonQuery.ToArray(), jsonQueryAdditions!, query?.ResultsUnique!);
            return ConvertListToRecords<T>(ListToConvert, propertyMappings);
        }
        catch (Exception jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }

        return default;
    }

    private void CollectBinaryExpressions<T>(Expression expression, Expression<Func<T, bool>> predicate, List<string> jsonQueries) where T : class
    {
        if (expression is BinaryExpression binaryExpr && binaryExpr.NodeType == ExpressionType.OrElse)
        {
            // Split the OR condition into separate expressions
            var left = binaryExpr.Left;
            var right = binaryExpr.Right;

            // Process left and right expressions recursively
            CollectBinaryExpressions(left, predicate, jsonQueries);
            CollectBinaryExpressions(right, predicate, jsonQueries);
        }
        else
        {
            // If the expression is a single condition, create a query for it
            _ = expression.ToString();
            _ = predicate.ToString();

            string jsonQuery = GetJsonQueryFromExpression(Expression.Lambda<Func<T, bool>>(expression, predicate.Parameters));
            jsonQueries.Add(jsonQuery);
        }
    }

    private static object ConvertValueToType(object value, Type targetType)
    {
        if (targetType == typeof(Guid) && value is string stringValue)
            return Guid.Parse(stringValue);

        return Convert.ChangeType(value, targetType);
    }


    private static List<TRecord> ConvertListToRecords<TRecord>(IList<Dictionary<string, object>> listToConvert, Dictionary<string, string> propertyMappings)
    {
        var records = new List<TRecord>();
        var recordType = typeof(TRecord);

        foreach (var item in listToConvert)
        {
            var record = Activator.CreateInstance<TRecord>();
            foreach (var kvp in item)
                if (propertyMappings.TryGetValue(kvp.Key, out var propertyName))
                {
                    var property = recordType.GetProperty(propertyName);
                    var value = ManagerHelper.GetValueFromValueKind(kvp.Value);
                    property?.SetValue(record, ConvertValueToType(value!, property.PropertyType));
                }
            records.Add(record);
        }
        return records;
    }

    private static TRecord ConvertIndexedDbRecordToCRecord<TRecord>(Dictionary<string, object> item, Dictionary<string, string> propertyMappings)
    {
        var recordType = typeof(TRecord);
        var record = Activator.CreateInstance<TRecord>();

        foreach (var kvp in item)
            if (propertyMappings.TryGetValue(kvp.Key, out var propertyName))
            {
                var property = recordType.GetProperty(propertyName);
                var value = ManagerHelper.GetValueFromValueKind(kvp.Value);
                property?.SetValue(record, ConvertValueToType(value!, property.PropertyType));
            }
        return record;
    }

    private static string GetJsonQueryFromExpression<T>(Expression<Func<T, bool>> predicate) where T : class
    {
        var serializerSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        var conditions = new List<JObject>();
        var orConditions = new List<List<JObject>>();

        void TraverseExpression(Expression expression, bool inOrBranch = false)
        {
            if (expression is BinaryExpression binaryExpression)
                if (binaryExpression.NodeType == ExpressionType.AndAlso)
                {
                    TraverseExpression(binaryExpression.Left, inOrBranch);
                    TraverseExpression(binaryExpression.Right, inOrBranch);
                }
                else if (binaryExpression.NodeType == ExpressionType.OrElse)
                {
                    if (inOrBranch)
                        throw new InvalidOperationException("Nested OR conditions are not supported.");

                    TraverseExpression(binaryExpression.Left, !inOrBranch);
                    TraverseExpression(binaryExpression.Right, !inOrBranch);
                }
                else
                    AddCondition(binaryExpression, inOrBranch);
            else if (expression is MethodCallExpression methodCallExpression)
                AddCondition(methodCallExpression, inOrBranch);
        }

        void AddCondition(Expression expression, bool inOrBranch)
        {
            if (expression is BinaryExpression binaryExpression)
            {
                var operation = binaryExpression.NodeType.ToString();

                if (binaryExpression.Left is MemberExpression leftMember && binaryExpression.Right is ConstantExpression rightConstant)
                    AddConditionInternal(leftMember, rightConstant, operation, inOrBranch);
                else if (binaryExpression.Left is ConstantExpression leftConstant && binaryExpression.Right is MemberExpression rightMember)
                {
                    // Swap the order of the left and right expressions and the operation
                    if (operation == "GreaterThan")
                        operation = "LessThan";
                    else if (operation == "LessThan")
                        operation = "GreaterThan";
                    else if (operation == "GreaterThanOrEqual")
                        operation = "LessThanOrEqual";
                    else if (operation == "LessThanOrEqual")
                        operation = "GreaterThanOrEqual";

                    AddConditionInternal(rightMember, leftConstant, operation, inOrBranch);
                }
            }
            else if (expression is MethodCallExpression methodCallExpression)
            {
                if (methodCallExpression.Method.DeclaringType == typeof(string) &&
                    (methodCallExpression.Method.Name == "Equals" || methodCallExpression.Method.Name == "Contains" || methodCallExpression.Method.Name == "StartsWith"))
                {
                    var left = methodCallExpression.Object as MemberExpression;
                    var right = methodCallExpression.Arguments[0] as ConstantExpression;
                    var operation = methodCallExpression.Method.Name;
                    var caseSensitive = true;

                    if (methodCallExpression.Arguments.Count > 1 &&
                        (methodCallExpression.Arguments[1] is ConstantExpression stringComparison && stringComparison.Value is StringComparison comparisonValue))
                        caseSensitive = comparisonValue == StringComparison.Ordinal || comparisonValue == StringComparison.CurrentCulture;

                    AddConditionInternal(left, right, operation == "Equals" ? "StringEquals" : operation, inOrBranch, caseSensitive);
                }
            }
        }

        void AddConditionInternal(MemberExpression? left, ConstantExpression? right, string operation, bool inOrBranch, bool caseSensitive = false)
        {
            if (left != null && right != null)
            {
                var propertyInfo = typeof(T).GetProperty(left.Member.Name);
                if (propertyInfo != null)
                {
                    bool index = propertyInfo.GetCustomAttributes(typeof(BlazorizedIndexAttribute), false).Length == 0;
                    bool unique = propertyInfo.GetCustomAttributes(typeof(BlazorizedUniqueIndexAttribute), false).Length == 0;
                    bool primary = propertyInfo.GetCustomAttributes(typeof(BlazorizedPrimaryKeyAttribute), false).Length == 0;

                    if (index == true && unique == true && primary == true)
                        throw new InvalidOperationException($"Property '{propertyInfo.Name}' does not have the IndexDbAttribute.");

                    string? columnName = null;

                    if (index == false)
                        columnName = propertyInfo.GetPropertyColumnName<BlazorizedIndexAttribute>();
                    else if (unique == false)
                        columnName = propertyInfo.GetPropertyColumnName<BlazorizedUniqueIndexAttribute>();
                    else if (primary == false)
                        columnName = propertyInfo.GetPropertyColumnName<BlazorizedPrimaryKeyAttribute>();

                    bool _isString = false;
                    JToken? valSend = null;
                    if (right != null && right.Value != null)
                    {
                        valSend = JToken.FromObject(right.Value);
                        _isString = right.Value is string;
                    }

                    var jsonCondition = new JObject
                    {
                        { "property", columnName },
                        { "operation", operation },
                        { "value", valSend },
                        { "isString", _isString },
                        { "caseSensitive", caseSensitive }
                    };

                    if (inOrBranch)
                    {
                        var currentOrConditions = orConditions.LastOrDefault();
                        if (currentOrConditions == null)
                        {
                            currentOrConditions = [];
                            orConditions.Add(currentOrConditions);
                        }
                        currentOrConditions.Add(jsonCondition);
                    }
                    else
                        conditions.Add(jsonCondition);
                }
            }
        }

        TraverseExpression(predicate.Body);

        if (conditions.Count != 0)
            orConditions.Add(conditions);

        return JsonConvert.SerializeObject(orConditions, serializerSettings);
    }

    public class QuotaUsage
    {
        public long Quota { get; set; }
        public long Usage { get; set; }
    }

    /// <summary>
    /// Returns Mb
    /// </summary>
    /// <returns></returns>
    public async Task<(double quota, double usage)> GetStorageEstimateAsync()
    {
        var storageInfo = await CallJavascriptNoTransaction<QuotaUsage>(IndexedDbFunctions.GET_STORAGE_ESTIMATE);

        double quotaInMB = ConvertBytesToMegabytes(storageInfo.Quota);
        double usageInMB = ConvertBytesToMegabytes(storageInfo.Usage);
        return (quotaInMB, usageInMB);
    }


    private static double ConvertBytesToMegabytes(long bytes) => (double)bytes / (1024 * 1024);


    public async Task<IEnumerable<T>> GetAll<T>() where T : class
    {
        var trans = GenerateTransaction(null);

        try
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();
            IList<Dictionary<string, object>>? ListToConvert = await CallJavascript<IList<Dictionary<string, object>>>(IndexedDbFunctions.TOARRAY, trans, DbName, schemaName);

            var resultList = ConvertListToRecords<T>(ListToConvert, propertyMappings);
            return resultList;
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }

        return Enumerable.Empty<T>();
    }

    public async Task<Guid> Delete<T>(T item, Action<BlazorDbEvent>? action = null) where T : class
    {
        var trans = GenerateTransaction(action);
        try
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute)));
            if (primaryKeyProperty != null)
            {
                object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);
                if (primaryKeyValue != null)
                {
                    UpdateRecord<Dictionary<string, object?>> record = new()
                    {
                        Key = primaryKeyValue,
                        DbName = this.DbName,
                        StoreName = schemaName,
                        Record = convertedRecord
                    };

                    // Get the primary key value of the item
                    await CallJavascriptVoid(IndexedDbFunctions.DELETE_ITEM, trans, record);
                }
                else
                    throw new ArgumentException("Item being Deleted must have a key.");
            }
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }
        return trans;
    }

    public async Task<int> DeleteRange<TResult>(IEnumerable<TResult> items) where TResult : class
    {
        List<object> keys = [];

        foreach (var item in items)
        {
            PropertyInfo? primaryKeyProperty = typeof(TResult).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(BlazorizedPrimaryKeyAttribute))) ?? throw new InvalidOperationException("No primary key property found with PrimaryKeyDbAttribute.");
            object? primaryKeyValue = primaryKeyProperty.GetValue(item);

            if (primaryKeyValue != null)
                keys.Add(primaryKeyValue);
        }
        string schemaName = SchemaHelper.GetSchemaName<TResult>();

        var trans = GenerateTransaction(null);

        var data = new { DbName, StoreName = schemaName, Keys = keys };

        try
        {
            var deletedCount = await CallJavascript<int>(IndexedDbFunctions.BULK_DELETE, trans, data.DbName, data.StoreName, data.Keys);
            return deletedCount;
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }

        return 0;
    }


    /// <summary>
    /// Clears all data from a Table but keeps the table
    /// </summary>
    /// <param name="storeName"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    public async Task<Guid> ClearTable(string storeName, Action<BlazorDbEvent>? action = null)
    {
        var trans = GenerateTransaction(action);
        try
        {
            await CallJavascriptVoid(IndexedDbFunctions.CLEAR_TABLE, trans, DbName, storeName);
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }
        return trans;
    }

    public async Task<Guid> ClearTable<T>(Action<BlazorDbEvent>? action = null) where T : class
    {
        var trans = GenerateTransaction(action);
        try
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            await CallJavascriptVoid(IndexedDbFunctions.CLEAR_TABLE, trans, DbName, schemaName);
        }
        catch (JSException jse)
        {
            RaiseEvent(trans, true, jse.Message);
        }
        return trans;
    }

    /// <summary>
    /// Clears all data from a Table but keeps the table
    /// Wait for response
    /// </summary>
    /// <param name="storeName"></param>
    /// <returns></returns>
    public async Task<BlazorDbEvent> ClearTableAsync(string storeName)
    {
        var trans = GenerateTransaction();
        try
        {
            await CallJavascriptVoid(IndexedDbFunctions.CLEAR_TABLE, trans.trans, DbName, storeName);
        }
        catch (JSException jse)
        {
            RaiseEvent(trans.trans, true, jse.Message);
        }
        return await trans.task;
    }

    [JSInvokable("BlazorDBCallback")]
    public void CalledFromJS(Guid transaction, bool failed, string message)
    {
        if (transaction != Guid.Empty)
        {
            _transactions.TryGetValue(transaction, out WeakReference<Action<BlazorDbEvent>>? r);
            _taskTransactions.TryGetValue(transaction, out TaskCompletionSource<BlazorDbEvent>? t);
            if (r != null && r.TryGetTarget(out Action<BlazorDbEvent>? action))
            {
                action?.Invoke(new BlazorDbEvent()
                {
                    Transaction = transaction,
                    Message = message,
                    Failed = failed
                });
                _transactions.Remove(transaction);
            }
            else if (t != null)
            {
                t.TrySetResult(new BlazorDbEvent()
                {
                    Transaction = transaction,
                    Message = message,
                    Failed = failed
                });
                _taskTransactions.Remove(transaction);
            }
            else
                RaiseEvent(transaction, failed, message);
        }
    }

    //async Task<TResult> CallJavascriptNoTransaction<TResult>(string functionName, params obj[] args)
    //{
    //    return await _jsRuntime.InvokeAsync<TResult>($"{InteropPrefix}.{functionName}", args);
    //}

    async Task<TResult> CallJavascriptNoTransaction<TResult>(string functionName, params object[] args)
    {
        var mod = await GetModule(_jsRuntime);
        return await mod.InvokeAsync<TResult>($"{functionName}", args);
    }


    private const string dynamicJsCaller = "DynamicJsCaller";
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="functionName"></param>
    /// <param name="transaction"></param>
    /// <param name="timeout">in ms</param>
    /// <param name="args"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<TResult> CallJS<TResult>(string functionName, double Timeout, params object[] args)
    {
        List<object> modifiedArgs = new(args);
        modifiedArgs.Insert(0, $"{InteropPrefix}.{functionName}");

        Task<JsResponse<TResult>> task = _jsRuntime.InvokeAsync<JsResponse<TResult>>(dynamicJsCaller, [.. modifiedArgs]).AsTask();
        Task delay = Task.Delay(TimeSpan.FromMilliseconds(Timeout));

        if (await Task.WhenAny(task, delay) == task)
        {
            JsResponse<TResult> response = await task;
            if (response.Success)
                return response.Data;
            else
                throw new ArgumentException(response.Message);
        }
        else
        {
            throw new ArgumentException("Timed out after 1 minute");
        }
    }

    //public async Task<TResult> CallJS<TResult>(string functionName, JsSettings Settings, params obj[] args)
    //{
    //    var newArgs = GetNewArgs(Settings.Transaction, args);

    //    Task<JsResponse<TResult>> task = _jsRuntime.InvokeAsync<JsResponse<TResult>>($"{InteropPrefix}.{functionName}", newArgs).AsTask();
    //    Task delay = Task.Delay(TimeSpan.FromMilliseconds(Settings.Timeout));

    //    if (await Task.WhenAny(task, delay) == task)
    //    {
    //        JsResponse<TResult> response = await task;
    //        if (response.Success)
    //            return response.Data;
    //        else
    //            throw new ArgumentException(response.Message);
    //    }
    //    else
    //    {
    //        throw new ArgumentException("Timed out after 1 minute");
    //    }
    //}



    //async Task<TResult> CallJavascript<TResult>(string functionName, Guid transaction, params obj[] args)
    //{
    //    var newArgs = GetNewArgs(transaction, args);
    //    return await _jsRuntime.InvokeAsync<TResult>($"{InteropPrefix}.{functionName}", newArgs);
    //}
    //async Task CallJavascriptVoid(string functionName, Guid transaction, params obj[] args)
    //{
    //    var newArgs = GetNewArgs(transaction, args);
    //    await _jsRuntime.InvokeVoidAsync($"{InteropPrefix}.{functionName}", newArgs);
    //}

    async Task<TResult> CallJavascript<TResult>(string functionName, Guid transaction, params object[] args)
    {
        var mod = await GetModule(_jsRuntime);
        var newArgs = GetNewArgs(transaction, args);
        return await mod.InvokeAsync<TResult>($"{functionName}", newArgs);
    }
    async Task CallJavascriptVoid(string functionName, Guid transaction, params object[] args)
    {
        var mod = await GetModule(_jsRuntime);
        var newArgs = GetNewArgs(transaction, args);
        await mod.InvokeVoidAsync($"{functionName}", newArgs);
    }



    object[] GetNewArgs(Guid transaction, params object[] args)
    {
        var newArgs = new object[args.Length + 2];
        newArgs[0] = _objReference;
        newArgs[1] = transaction;
        for (var i = 0; i < args.Length; i++)
            newArgs[i + 2] = args[i];
        return newArgs;
    }

    (Guid trans, Task<BlazorDbEvent> task) GenerateTransaction()
    {
        bool generated = false;
        TaskCompletionSource<BlazorDbEvent> tcs = new();
        Guid transaction;
        do
        {
            transaction = Guid.NewGuid();
            if (_taskTransactions.TryAdd(transaction, tcs))
                generated = true;
        } while (!generated);
        return (transaction, tcs.Task);
    }

    Guid GenerateTransaction(Action<BlazorDbEvent>? action)
    {
        bool generated = false;
        Guid transaction;
        do
        {
            transaction = Guid.NewGuid();
            if (!_transactions.ContainsKey(transaction))
            {
                generated = true;
                _transactions.Add(transaction, new WeakReference<Action<BlazorDbEvent>>(action!));
            }
        } while (!generated);
        return transaction;
    }

    void RaiseEvent(Guid transaction, bool failed, string message)
        => ActionCompleted?.Invoke(this, new BlazorDbEvent { Transaction = transaction, Failed = failed, Message = message });

}
