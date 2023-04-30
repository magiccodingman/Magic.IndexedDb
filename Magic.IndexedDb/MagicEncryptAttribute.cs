using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MagicEncryptAttribute : Attribute
    {
        public MagicEncryptAttribute()
        {
        }

        public void Validate(PropertyInfo property)
        {
            if (property.PropertyType != typeof(string))
            {
                throw new ArgumentException("EncryptDbAttribute can only be used on string properties");
            }
        }
    }
}
