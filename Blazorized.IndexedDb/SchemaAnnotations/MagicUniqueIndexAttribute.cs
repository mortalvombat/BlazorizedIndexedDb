using Blazorized.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Blazorized.IndexedDb
{
    [AttributeUsage(AttributeTargets.Property)]
    public class BlazorizedUniqueIndexAttribute : Attribute
    {
        [BlazorizedColumnNameDesignator]
        public string ColumnName { get; }

        public BlazorizedUniqueIndexAttribute(string columnName = null)
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
