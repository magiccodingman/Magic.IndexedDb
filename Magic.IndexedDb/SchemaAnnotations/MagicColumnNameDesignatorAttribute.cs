using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.SchemaAnnotations
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MagicColumnNameDesignatorAttribute : Attribute
    {
        private static bool isApplied = false;

        public MagicColumnNameDesignatorAttribute()
        {
            if (isApplied)
            {
                throw new InvalidOperationException("The SingleProperty attribute can only be applied to one property.");
            }
            isApplied = true;
        }
    }
}
