using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.SchemaAnnotations;

[AttributeUsage(AttributeTargets.Property)]
public class MagicNameAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }

    public MagicNameAttribute(string columnName)
    {
        if (!string.IsNullOrWhiteSpace(columnName))
        {
            ColumnName = columnName;
        }
        else
        {
            throw new Exception("You have a MagicName attribute with no column name string provided!");
        }
    }
}