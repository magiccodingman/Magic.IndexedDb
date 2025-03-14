using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb
{
    /// <summary>
    /// sets as the primary key
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MagicPrimaryKeyAttribute : Attribute, IColumnNamed
    {
        public string ColumnName { get; }

        public MagicPrimaryKeyAttribute(string columnName = null)
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
