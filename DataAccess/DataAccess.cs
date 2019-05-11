using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DataAccess
{
    public class KVTable
    {
        [PrimaryKey]
        public string Key { get; set; }
        public string Value { get; set; }
        public DateTimeOffset TimeStamp { get; set; }
    }

    public class CoordinatorLookupTable
    {
        [PrimaryKey]
        public string Key { get; set; }
        public int RingSize { get; set; }
    }

    /// <summary>
    /// Pipline used for accessing underlying data storage.
    /// </summary>
    public static class SqliteDatabase
    {
        public static SQLiteAsyncConnection Database { get; set; }

        public static SQLiteAsyncConnection CoordinatorLookup { get; set; }

        public static async Task InitializeDatabase()
        {
            var FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KVDatabase.db");
            Database = new SQLiteAsyncConnection(FilePath);
            if (File.Exists(FilePath))
                Database = new SQLiteAsyncConnection(FilePath, SQLiteOpenFlags.ReadWrite);

            else
                await Database.CreateTableAsync<KVTable>();
        }

        public static async Task InitializeLookupTable()
        {
            var FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KVDatabaseCoordCache.db");
            CoordinatorLookup = new SQLiteAsyncConnection(FilePath);
            if (File.Exists(FilePath))
                CoordinatorLookup = new SQLiteAsyncConnection(FilePath, SQLiteOpenFlags.ReadWrite);

            else
                await CoordinatorLookup.CreateTableAsync<CoordinatorLookupTable>();
        }

        public static async Task<bool> InsertLookupEntry(string key, int ringSize)
        {
            try
            {
                int s = await CoordinatorLookup.InsertAsync(new CoordinatorLookupTable()
                {
                    Key = key,
                    RingSize = ringSize
                });
                return true;
            }
            catch (SQLiteException) { return false; }
        }
        
        public static async Task<CoordinatorLookupTable> GetLookupEntryAsync(string key)
        {
            var Query = await CoordinatorLookup.Table<CoordinatorLookupTable>().Where(v => v.Key.Equals(key)).ToListAsync();
            return Query[0];
        }

        public static async Task UpdateLookupEntry(string key, int ringSize)
        {
            int s = await CoordinatorLookup.UpdateAsync(new CoordinatorLookupTable()
            {
                Key = key,
                RingSize = ringSize,
            });
        }

        public static async Task DeleteLookupEntry(string key)
        {
            int s = await CoordinatorLookup.DeleteAsync(new CoordinatorLookupTable()
            {
                Key = key,
            });
        }

        public static async Task<bool> InsertKeyValuePairAsync(string key, string value)
        {
            try
            {
                int s = await Database.InsertAsync(new KVTable()
                {
                    Key = key,
                    Value = value,
                    TimeStamp = DateTimeOffset.Now
                });
                return true;
            }
            catch (SQLiteException) { return false; }
        }

        public static async Task<KVTable> GetValueAsync(string key)
        {
            var Query = await Database.Table<KVTable>().Where(v => v.Key.Equals(key)).ToListAsync();
            return Query[0];
        }

        public static async Task UpdateValue(string key, string value)
        {
            int s = await Database.UpdateAsync(new KVTable()
            {
                Key = key,
                Value = value,
                TimeStamp = DateTimeOffset.Now
            });
        }

        public static async Task DeleteValue(string key)
        {
            int s = await Database.DeleteAsync(new KVTable()
            {
                Key = key,
            });
        }
    }
}
