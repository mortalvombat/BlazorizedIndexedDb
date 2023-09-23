using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Blazorized.IndexedDb.SchemaAnnotations
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class BlazorizedColumnNameDesignatorAttribute : Attribute
    {
        private static bool isApplied = false;

        public BlazorizedColumnNameDesignatorAttribute()
        {
            if (isApplied)
            {
                throw new InvalidOperationException("The SingleProperty attribute can only be applied to one property.");
            }
            isApplied = true;
        }
    }
}
