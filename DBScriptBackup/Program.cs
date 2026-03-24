using System.Text.Json;

namespace DBScriptBackup

{
    static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Validate command line arguments
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: program.exe <path-to-config.json>");
                    Console.WriteLine("\nExample config.json structure:");
                    Console.WriteLine(@"{
  ""serverName"": ""ServerName"",
  ""basePath"": ""C:\\repo\\Project"",
  ""databases"": [
    {
      ""name"": ""Main"",
      ""includeData"": true
    },
    {
      ""name"": ""Secondary"",
      ""includeData"": false
    }
  ]
}");
                    return;
                }

                string configPath = args[0];

                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"Error: Configuration file not found: {configPath}");
                    return;
                }

                Console.WriteLine($"Loading configuration from: {configPath}");
                string jsonContent = File.ReadAllText(configPath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var config = JsonSerializer.Deserialize<BackupConfiguration>(jsonContent, options);

                if (config == null)
                {
                    Console.WriteLine("Error: Failed to parse configuration file");
                    return;
                }

                // Validate configuration
                if (string.IsNullOrWhiteSpace(config.ServerName))
                {
                    Console.WriteLine("Error: serverName is required in configuration");
                    return;
                }

                if (string.IsNullOrWhiteSpace(config.BasePath))
                {
                    Console.WriteLine("Error: basePath is required in configuration");
                    return;
                }

                if (config.Databases == null || config.Databases.Length == 0)
                {
                    Console.WriteLine("Error: At least one database must be specified");
                    return;
                }

                if (config.Databases.Any(x => string.IsNullOrWhiteSpace(x.Name)))
                {
                    Console.WriteLine($"Error: No databases can have blank names");
                    return;
                }

                // Display configuration
                Console.WriteLine($"Server: {config.ServerName}");
                Console.WriteLine($"Base Path: {config.BasePath}");
                Console.WriteLine($"Databases:");
                foreach (var db in config.Databases)
                {
                    Console.WriteLine($"  - {db.Name} (Data: {(db.IncludeData ? "Yes" : "No")})");
                }
                Console.WriteLine();

                // Create timestamped backup directory
                string timestampedPath = Path.Combine(config.BasePath, $"DB-{DateTime.Now:yyyyMMddHHmmss}");

                // Script each database
                foreach (var dbConfig in config.Databases)
                {
                    Console.WriteLine($"\n=== Processing Database: {dbConfig.Name} (Include Data: {dbConfig.IncludeData}) ===");
                    SqlDatabaseScripter.ScriptEntireDB(config.ServerName, timestampedPath, dbConfig.Name, dbConfig.IncludeData);
                }

                Console.WriteLine("\nAll databases have been scripted successfully!");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Application error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}