using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb;

/// <summary>
/// Creates a unique key
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class MagicUniqueIndexAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }

    public MagicUniqueIndexAttribute(string columnName = null)
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