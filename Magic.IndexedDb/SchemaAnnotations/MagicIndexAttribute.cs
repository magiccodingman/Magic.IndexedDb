using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb.SchemaAnnotations;

/// <summary>
/// Indexes this key
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class MagicIndexAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }

    public MagicIndexAttribute(string columnName = null)
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