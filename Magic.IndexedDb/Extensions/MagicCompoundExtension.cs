using Magic.IndexedDb.Models;
using System.Linq.Expressions;

namespace Magic.IndexedDb;

internal static class MagicCompoundExtension
{
    public static IMagicCompoundIndex CreateIndex<T>(params Expression<Func<T, object>>[] keySelectors)
    {
        return InternalMagicCompoundIndex<T>.Create(keySelectors);
    }

    public static IMagicCompoundKey CreateKey<T>(bool autoIncrement, params Expression<Func<T, object>>[] keySelectors)
    {
        return InternalMagicCompoundKey<T>.Create(autoIncrement, keySelectors);
    }
}