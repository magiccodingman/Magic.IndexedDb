namespace Magic.IndexedDb;

/// <summary>
/// sets as the primary key
/// </summary>
/*[AttributeUsage(AttributeTargets.Property)]
public class MagicPrimaryKeyAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }
    public bool AutoIncrement { get; }

    /// <summary>
    ///
    /// </summary>
    /// <param name="autoIncrement">whether the primary key automatically increments when new rows are added.</param>
    /// <param name="columnName"></param>
    public MagicPrimaryKeyAttribute(bool autoIncrement, string columnName = null)
    {
        AutoIncrement = autoIncrement;
        if (!String.IsNullOrWhiteSpace(columnName))
        {
            ColumnName = columnName;
        }
        else
        {
            ColumnName = null;
        }
    }
}*/