using Magic.IndexedDb.Interfaces;

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