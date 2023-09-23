using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blazorized.IndexedDb.SchemaAnnotations;

namespace Blazorized.IndexedDb
{
    [AttributeUsage(AttributeTargets.Property)]
    public class BlazorizedPrimaryKeyAttribute : Attribute
    {
        [BlazorizedColumnNameDesignator]
        public string ColumnName { get; }

        public BlazorizedPrimaryKeyAttribute(string columnName = null)
        {
            if (!String.IsNullOrWhiteSpace(columnName))
            {
                ColumnName = columnName;
            }
            else
            {
                ColumnName = null;
            }
        }
    }
}
