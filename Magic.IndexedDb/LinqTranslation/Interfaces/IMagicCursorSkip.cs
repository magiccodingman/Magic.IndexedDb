namespace Magic.IndexedDb;

public interface IMagicCursorSkip<T> : IMagicExecute<T> where T : class
{
    //IMagicCursorSkip<T> Skip(int amount);
    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();

}