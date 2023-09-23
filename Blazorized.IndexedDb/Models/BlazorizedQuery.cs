using Blazorized.IndexedDb.Helpers;
using Blazorized.IndexedDb.SchemaAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Blazorized.IndexedDb.Models;

public class BlazorizedQuery<T>(string schemaName, IndexedDbManager manager) where T : class
{
    public string SchemaName { get; } = schemaName;
    public List<string> JsonQueries { get; } = new List<string>();
    public IndexedDbManager Manager { get; } = manager;

    public List<StoredBlazorizedQuery> StoredBlazorizedQueries { get; set; } = new List<StoredBlazorizedQuery>();

    public bool ResultsUnique { get; set; } = true;

    /// <summary>
    /// Return a list of items in which the items do not have to be unique. Therefore, you can get 
    /// duplicate instances of an object depending on how you write your query.
    /// </summary>
    /// <param name="amount"></param>
    /// <returns></returns>
    public BlazorizedQuery<T> ResultsNotUnique()
    {
        ResultsUnique = false;
        return this;
    }

    public BlazorizedQuery<T> Take(int amount)
    {
        StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
        smq.Name = BlazorizedQueryFunctions.Take;
        smq.IntValue = amount;
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    public BlazorizedQuery<T> TakeLast(int amount)
    {
        StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
        smq.Name = BlazorizedQueryFunctions.Take_Last;
        smq.IntValue = amount;
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    public BlazorizedQuery<T> Skip(int amount)
    {
        StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
        smq.Name = BlazorizedQueryFunctions.Skip;
        smq.IntValue = amount;
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    //public BlazorizedQuery<T> Reverse()
    //{
    //    StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
    //    smq.Name = BlazorizedQueryFunctions.Reverse;
    //    storedBlazorizedQueries.Add(smq);
    //    return this;
    //}

    // Not yet working
    private BlazorizedQuery<T> First()
    {
        StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
        smq.Name = BlazorizedQueryFunctions.First;
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    // Not yet working
    private BlazorizedQuery<T> Last()
    {
        StoredBlazorizedQuery smq = new StoredBlazorizedQuery();
        smq.Name = BlazorizedQueryFunctions.Last;
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    public async Task<IEnumerable<T>> Execute() => await Manager.WhereV2<T>(SchemaName, JsonQueries, this) ?? Enumerable.Empty<T>();

    public async Task<int> Count()
    {
        var result = await Manager.WhereV2<T>(SchemaName, JsonQueries, this);
        int num = result?.Count ?? 0;
        return num;
    }


    // Not currently available in Dexie version 1,2, or 3
    public BlazorizedQuery<T> OrderBy(Expression<Func<T, object>> predicate)
    {
        var memberExpression = GetMemberExpressionFromLambda(predicate);
        var propertyInfo = memberExpression.Member as PropertyInfo;

        if (propertyInfo == null)
            throw new ArgumentException("The expression must represent a single property access.");

        var indexDbAttr = propertyInfo.GetCustomAttribute<BlazorizedIndexAttribute>();
        var uniqueIndexDbAttr = propertyInfo.GetCustomAttribute<BlazorizedUniqueIndexAttribute>();
        var primaryKeyDbAttr = propertyInfo.GetCustomAttribute<BlazorizedPrimaryKeyAttribute>();

        if (indexDbAttr == null && uniqueIndexDbAttr == null && primaryKeyDbAttr == null)
            throw new ArgumentException("The selected property must have either BlazorizedIndexAttribute, BlazorizedUniqueIndexAttribute, or BlazorizedPrimaryKeyAttribute.");

        string? columnName = null;

        if (indexDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedIndexAttribute>();
        else if (primaryKeyDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedPrimaryKeyAttribute>();
        else if (uniqueIndexDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedUniqueIndexAttribute>();

        var smq = new StoredBlazorizedQuery
        {
            Name = BlazorizedQueryFunctions.Order_By,
            StringValue = columnName
        };
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

    // Not currently available in Dexie version 1,2, or 3
    public BlazorizedQuery<T> OrderByDescending(Expression<Func<T, object>> predicate)
    {
        var memberExpression = GetMemberExpressionFromLambda(predicate);
        var propertyInfo = memberExpression.Member as PropertyInfo ?? throw new ArgumentException("The expression must represent a single property access.");
        var indexDbAttr = propertyInfo.GetCustomAttribute<BlazorizedIndexAttribute>();
        var uniqueIndexDbAttr = propertyInfo.GetCustomAttribute<BlazorizedUniqueIndexAttribute>();
        var primaryKeyDbAttr = propertyInfo.GetCustomAttribute<BlazorizedPrimaryKeyAttribute>();

        if (indexDbAttr == null && uniqueIndexDbAttr == null && primaryKeyDbAttr == null)
            throw new ArgumentException("The selected property must have either BlazorizedIndexAttribute, BlazorizedUniqueIndexAttribute, or BlazorizedPrimaryKeyAttribute.");

        string? columnName = null;

        if (indexDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedIndexAttribute>();
        else if (primaryKeyDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedPrimaryKeyAttribute>();
        else if (uniqueIndexDbAttr != null)
            columnName = propertyInfo.GetPropertyColumnName<BlazorizedUniqueIndexAttribute>();

        StoredBlazorizedQuery smq = new()
        {
            Name = BlazorizedQueryFunctions.Order_By_Descending,
            StringValue = columnName
        };
        StoredBlazorizedQueries.Add(smq);
        return this;
    }

#pragma warning disable CS0693 // Mark members as static
    private MemberExpression GetMemberExpressionFromLambda<T>(Expression<Func<T, object>> expression)
#pragma warning restore CS0693 // Mark members as static
    {
        if (expression.Body is MemberExpression expression1)
            return expression1;
        else if (expression.Body is UnaryExpression expression2 && expression2.Operand is MemberExpression expression3)
            return expression3;
        else
            throw new ArgumentException("The expression must represent a single property access.");
    }

}
