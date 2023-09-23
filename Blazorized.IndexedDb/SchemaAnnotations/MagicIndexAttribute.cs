using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blazorized.IndexedDb.SchemaAnnotations;

namespace Blazorized.IndexedDb.SchemaAnnotations
{
    [AttributeUsage(AttributeTargets.Property)]
    public class BlazorizedIndexAttribute : Attribute
    {
        [BlazorizedColumnNameDesignator]
        public string ColumnName { get; }

        public BlazorizedIndexAttribute(string columnName = null)
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
