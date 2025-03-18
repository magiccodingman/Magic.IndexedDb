namespace Magic.IndexedDb
{ 
    internal struct IndexedDbFunctions
    {
        public const string CREATE_DATABASES = "createDatabases";
        public const string CREATE_LEGACY = "createDb";
        public const string CLOSE_ALL = "closeAll";
        public const string DELETE_DB = "deleteDb";
        public const string ADD_ITEM = "addItem";
        public const string BULKADD_ITEM = "bulkAddItem";
        public const string COUNT_TABLE = "countTable";

        // TODO: not referenced?
        public const string PUT_ITEM = "putItem";

        // hooking up
        public const string BULKPUT_ITEM = "bulkPutItems";


        public const string UPDATE_ITEM = "updateItem";
        public const string BULKADD_UPDATE = "bulkUpdateItem";
        public const string BULK_DELETE = "bulkDelete";
        public const string DELETE_ITEM = "deleteItem";
        public const string CLEAR_TABLE = "clear";
        public const string FIND_ITEM = "findItem";
        public const string TOARRAY = "toArray";
        public const string GET_STORAGE_ESTIMATE = "getStorageEstimate";
        public const string MAGIC_QUERY_ASYNC = "magicQueryAsync";
        public const string MAGIC_QUERY_YIELD = "magicQueryYield";
    }
}