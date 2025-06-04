using Magic.IndexedDb.Interfaces;
using System.Linq.Expressions;

namespace Magic.IndexedDb;

public class MagicTableTool<T> where T : class, IMagicTableBase, new()
{
    protected IMagicCompoundIndex CreateCompoundIndex(params Expression<Func<T, object>>[] keySelectors)
    {
        return MagicCompoundExtension.CreateIndex<T>(keySelectors);
    }

    protected IMagicCompoundKey CreateCompoundKey(params Expression<Func<T, object>>[] keySelectors)
    {
        return MagicCompoundExtension.CreateKey(false, keySelectors);
    }

    protected IMagicCompoundKey CreatePrimaryKey(Expression<Func<T, object>> keySelector, bool autoIncrement)
    {
        return MagicCompoundExtension.CreateKey(autoIncrement, keySelector);
    }
}