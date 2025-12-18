using DBScriptBackup;
using Microsoft.SqlServer.Management.Smo;

namespace DBScriptBackup
{
    public class SqlDatabaseScripter
    {
        /// <summary>
        /// Scripts all table data to individual SQL files
        /// </summary>
        public static void ScriptAllTablesWithData(string serverName, string databaseName, string outputFolder)
        {
            try
            {
                // Create SMO server and database objects
                Server server = new Server(serverName);
                server.SetDefaultInitFields(typeof(Table), "IsSystemObject");
                server.SetDefaultInitFields(typeof(View), "IsSystemObject");
                server.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject");
                server.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject");

                Database database = server.Databases[databaseName];

                // Create data subfolder
                string dataFolder = Path.Combine(outputFolder, "Data");
                Directory.CreateDirectory(dataFolder);

                // Script each table's data
                foreach (Table table in database.Tables)
                {
                    if (table.IsSystemObject) continue;

                    //Console.WriteLine($"Scripting data for table: {table.Name}");

                    // Create scripter for data only
                    Scripter scripter = new Scripter(server);
                    scripter.Options.ScriptSchema = false;
                    scripter.Options.ScriptData = true;
                    scripter.Options.ToFileOnly = true;
                    scripter.Options.IncludeHeaders = false;
                    scripter.Options.NoCommandTerminator = true;
                    scripter.Options.FileName = Path.Combine(dataFolder, $"{table.Schema}.{table.Name}.sql");

                    Console.WriteLine($"Scripted data for table {table.Schema}.{table.Name}: {scripter.Options.FileName}");

                    // Generate and output scripts
                    foreach (string script in scripter.EnumScript(new SqlSmoObject[] { table }))
                    {
                        Console.WriteLine(script);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ScriptAllTablesWithData: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Scripts database objects into organized folders
        /// </summary>
        public static void ScriptDBObjectsIntoFolders(string serverName, string databaseName, Server server, Database database, string savePath)
        {
            try
            {
                Console.WriteLine($"Scripting database objects for server {serverName}, DB {databaseName}, save path {savePath}");

                // Collect all database objects to script
                SqlSmoObject[] objects = GetDatabaseObjects(database);

                foreach (SqlSmoObject scriptThis in objects)
                {
                    if (Util.IsSystemObjectGeneric(scriptThis))
                        continue;

                    string typeFolderName = scriptThis.GetType().Name;
                    Console.WriteLine($"Processing {typeFolderName}: {scriptThis}");

                    // Create folder for this object type if it doesn't exist
                    string typeFolder = Path.Combine(savePath, typeFolderName);
                    if (!Directory.Exists(typeFolder))
                    {
                        Directory.CreateDirectory(typeFolder);
                        Console.WriteLine($"Created directory: {typeFolder}");
                    }

                    // Clean up object name for filename
                    string scriptFileName = scriptThis.ToString().Replace("[", "").Replace("]", "") + ".SQL";
                    string scriptFilePath = Path.Combine(typeFolder, scriptFileName);

                    // Script DROP statements
                    ScriptObjectDrop(server, scriptThis, scriptFilePath);

                    // Script CREATE statements
                    ScriptObjectCreate(server, scriptThis, scriptFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ScriptDBObjectsIntoFolders: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets all database objects to be scripted
        /// </summary>
        private static SqlSmoObject[] GetDatabaseObjects(Database database)
        {
            var objects = new List<SqlSmoObject>();

            // Add different types of database objects
            foreach (Table table in database.Tables)
                objects.Add(table);

            foreach (View view in database.Views)
                objects.Add(view);

            foreach (StoredProcedure storedProc in database.StoredProcedures)
                objects.Add(storedProc);

            foreach (UserDefinedFunction function in database.UserDefinedFunctions)
                objects.Add(function);

            foreach (User user in database.Users)
                objects.Add(user);

            foreach (Schema schema in database.Schemas)
                objects.Add(schema);

            foreach (Synonym synonym in database.Synonyms)
                objects.Add(synonym);

            return objects.ToArray();
        }

        /// <summary>
        /// Scripts DROP statements for an object
        /// </summary>
        private static void ScriptObjectDrop(Server server, SqlSmoObject scriptObject, string filePath)
        {
            try
            {
                Scripter dropScripter = new Scripter(server);
                dropScripter.Options.AppendToFile = true;
                dropScripter.Options.AllowSystemObjects = false;
                dropScripter.Options.ClusteredIndexes = true;
                dropScripter.Options.DriAll = true;
                dropScripter.Options.ScriptDrops = true;
                dropScripter.Options.IncludeHeaders = false;
                dropScripter.Options.ToFileOnly = true;
                dropScripter.Options.Indexes = true;
                dropScripter.Options.WithDependencies = false;
                dropScripter.Options.FileName = filePath;

                foreach (string script in dropScripter.EnumScript(new SqlSmoObject[] { scriptObject }))
                {
                    Console.WriteLine(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scripting DROP for {scriptObject}: {ex.Message}");
            }
        }

        /// <summary>
        /// Scripts CREATE statements for an object
        /// </summary>
        private static void ScriptObjectCreate(Server server, SqlSmoObject scriptObject, string filePath)
        {
            try
            {
                Scripter createScripter = new Scripter(server);
                createScripter.Options.AppendToFile = true;
                createScripter.Options.AllowSystemObjects = false;
                createScripter.Options.ClusteredIndexes = true;
                createScripter.Options.DriAll = true;
                createScripter.Options.ScriptDrops = false;
                createScripter.Options.IncludeHeaders = false;
                createScripter.Options.ToFileOnly = true;
                createScripter.Options.Indexes = true;
                createScripter.Options.Permissions = true;
                createScripter.Options.WithDependencies = false;
                createScripter.Options.FileName = filePath;

                foreach (string script in createScripter.EnumScript(new SqlSmoObject[] { scriptObject }))
                {
                    Console.WriteLine(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scripting CREATE for {scriptObject}: {ex.Message}");
            }
        }

        /// <summary>
        /// Scripts entire database(s) with optional data
        /// </summary>
        public static void ScriptEntireDB(string serverName, string savePath, string dbName, bool includeData)
        {
            try
            {
                // Create folder

                string dbPath = Path.Combine(savePath, dbName);

                // Create SMO server and database objects
                Server server = new Server(serverName);
                if (server == null) throw new ArgumentException($"Server {serverName} not available");
                Database database = server.Databases[dbName];
                if (database == null) throw new ArgumentException($"Database {dbName} not available on specified server");

                // Create database folder
                Directory.CreateDirectory(dbPath);
                Console.WriteLine($"Created folder {dbPath} for database {dbName}");

                // Script database objects
                ScriptDBObjectsIntoFolders(serverName, dbName, server, database, dbPath);

                // Script data if requested
                if (includeData)
                {
                    string tableFolder = Path.Combine(dbPath, "Table");
                    ScriptAllTablesWithData(serverName, dbName, tableFolder);
                }

                ExecutionOrderScripter.GenerateExecutionOrderFile(database, dbPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ScriptEntireDB: {ex.Message}");
                throw;
            }
        }
    }
}