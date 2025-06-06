using System.Reflection;

namespace Magic.IndexedDb;

public interface IMagicCompoundIndex
{
    string[] ColumnNamesInCompoundIndex { get; }
    PropertyInfo[] PropertyInfos { get; }
}