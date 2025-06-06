namespace Magic.IndexedDb.LinqTranslation.Interfaces;
//public interface IMagicDatabaseGlobal
//{
//    Task DeleteAll();
//    Task ClearAll();
//}

public interface IMagicDatabaseScoped
{
    Task DeleteAsync();
    Task CloseAsync();

    Task<bool> IsOpenAsync();
    Task OpenAsync();
    Task<bool> DoesExistAsync();
    //Task Clear();
}