using System.Data.SQLite;
using System.Dynamic;

namespace BananaStore
{
    public delegate void DbCallback(dynamic message);

    public abstract class DatabaseCommand
    {
        public string TableName { get; set; }
        public ExpandoObject Record { get; set; }
        public DbCallback Callback { get; set; }

        protected DatabaseCommand(string tableName, 
            ExpandoObject record, DbCallback callback)
        {
            TableName = tableName;
            Record = record;    
            Callback = callback;    
        }

        public abstract void Execute(SQLiteConnection connection);
    }

    /// <summary>
    /// insert command
    /// </summary>
    
    public class InsertCommand : DatabaseCommand
    {
        public InsertCommand(string tableName,  ExpandoObject record, 
            DbCallback callback)
            : base(tableName, record, callback) { }

        public override void Execute(SQLiteConnection connection)
        {
            var dict = Record as IDictionary<string, object>;
            if (dict != null)
            {
                string columns = string.Join(",", dict.Keys);
                string values = string.Join(",", dict.Values);
                string paramNames = string.Join(",", dict.Keys.Select(k => "@" + k));

                string sql = $"INSERT INTO {TableName}({columns}) VALUES({paramNames})";

                using(var cmd = new SQLiteCommand(sql, connection))
                {
                    foreach(var pair in dict)
                    {
                        cmd.Parameters.AddWithValue("@"+pair.Key, pair.Value);
                    }
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Callback("Insert Success");

                    }catch(Exception ex)
                    {
                        Callback($"Exception: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// create command
    /// </summary>
    /// 

    public class CreateCommand : DatabaseCommand
    {
        public CreateCommand(string tableName, ExpandoObject recordTemplate,
            DbCallback callback)
            : base(tableName, recordTemplate, callback) { }

        private string GetTypeForValue(object value)
        {
            if (value is int) return "INTEGER";
            else if (value is string) return "TEXT";
            else if (value is double || value is float) return "REAL";
            else if (value is byte[]) return "BLOB";
            else throw new NotSupportedException($"Type {value.GetType().Name} not supported.");
        }

        public override void Execute(SQLiteConnection connection)
        {
            var dict = Record as IDictionary<string, object> ;
            if(dict != null)
            {
                var columnDefinitions = dict.Select(pair => {
                    string type = GetTypeForValue(pair.Value);
                    return $"{pair.Key} {type}";                
                });

                string columns = string.Join(", ", columnDefinitions) ;
                string sql = $"CREATE TABLE IF NOT EXISTS " +
                    $" {TableName}(ID integer PRIMARY KEY AUTOINCREMENT, " +
                    $"{columns})";

                using(var cmd = new SQLiteCommand(sql, connection))
                {
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Callback($"Table {TableName} created.");
                    } catch (Exception ex)
                    {
                        Callback($"Exception: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Database Manager
    /// </summary>

    public sealed class DatabaseManager
    {
        private static readonly Lazy<DatabaseManager> lazyInstance = new Lazy<DatabaseManager>(() => new DatabaseManager());
        private readonly Queue<DatabaseCommand> commandQueue = new Queue<DatabaseCommand>();
        private readonly Thread executorThread;
        private readonly string connectionString = "Data Source=sample.db;Version=3;";

        private DatabaseManager()
        {
            executorThread = new Thread(ExecuteCommands);
            executorThread.Start();
        }

        public static DatabaseManager Instance => lazyInstance.Value;

        public void EnqueueCommand(DatabaseCommand command)
        {
            lock (commandQueue)
            {
                commandQueue.Enqueue(command);
                Monitor.Pulse(commandQueue);
            }
        }

        private void ExecuteCommands()
        {
            while (true)
            {
                DatabaseCommand command = null;
                lock (commandQueue)
                {
                    while (commandQueue.Count == 0)
                    {
                        Monitor.Wait(commandQueue);
                    }
                    command = commandQueue.Dequeue();
                }

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    command.Execute(connection);
                }
            }
        }
    }

    internal class Program
    {
        private static DatabaseManager _dbManager;

        static void Main(string[] args)
        {
            _dbManager = DatabaseManager.Instance;
            dynamic banana = new ExpandoObject();
            banana.Name = "b1";
            banana.Type = "asddasdasd";
            banana.Price = 0.5;
            DbCallback createCallback = result => Console.WriteLine($"Create Result: {result}");
            var createCmd = new CreateCommand("BANANAS", banana, createCallback); 
            _dbManager.EnqueueCommand(createCmd);

            DbCallback insertCallback = result => Console.WriteLine($"INSERT Result: {result}");
            var insertCommand1 = new InsertCommand("BANANAS", banana, insertCallback);
            _dbManager.EnqueueCommand(insertCommand1);
            dynamic banana2 = new ExpandoObject();
            banana2.Name = "b1";
            banana2.Type = "asddasdasd";
            banana2.Price = 0.5;

            var insertCommand2 = new InsertCommand("BANANAS", banana2, insertCallback);
            _dbManager.EnqueueCommand(insertCommand2);
        }
    }
}