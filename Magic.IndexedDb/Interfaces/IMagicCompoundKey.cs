using System.Reflection;

namespace Magic.IndexedDb;

public interface IMagicCompoundKey
{
    string[] ColumnNamesInCompoundKey { get; }
    bool AutoIncrement { get; }
    PropertyInfo[] PropertyInfos { get; }
}