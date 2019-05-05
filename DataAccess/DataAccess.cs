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

    /// <summary>
    /// Pipline used for accessing underlying data storage.
    /// </summary>
    public static class SqliteDatabase
    {
        public static SQLiteAsyncConnection Database { get; set; }

        static SqliteDatabase()
        {
            var FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KVDatabase.db");
            Database = new SQLiteAsyncConnection(FilePath);
            if (File.Exists(FilePath))
            {
                var SyncDb = new SQLiteConnection(FilePath, SQLiteOpenFlags.ReadWrite);
                SyncDb.CreateTable<KVTable>();
            }
            else
            {
                var SyncDb = new SQLiteConnection(FilePath);
                SyncDb.CreateTable<KVTable>();
            }
        }

        public static async Task InsertKeyValuePairAsync(string key, string value)
        {
            int s = await Database.InsertAsync(new KVTable()
            {
                Key = key,
                Value = value,
                TimeStamp = DateTimeOffset.Now
            });
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
