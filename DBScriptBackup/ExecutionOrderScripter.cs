using Microsoft.SqlServer.Management.Smo;

namespace DBScriptBackup
{
    public static class ExecutionOrderScripter
    {
        /// <summary>
        /// Generates execution order file based on actual dependencies across all object types
        /// </summary>
        public static void GenerateExecutionOrderFile(Database database, string outputDirectory)
        {
            var executionOrder = new List<string>();

            executionOrder.Add("-- DATABASE SCRIPT EXECUTION ORDER");
            executionOrder.Add($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            executionOrder.Add($"-- Database: {database.Name}");
            executionOrder.Add("--");
            executionOrder.Add("-- This file lists scripts in dependency order.");
            executionOrder.Add("-- Execute in the exact order shown to avoid dependency errors.");
            executionOrder.Add("");

            Console.WriteLine("Building dependency graph...");
            // Build comprehensive dependency graph for all objects
            var allDependencies = BuildCompleteDependencyGraph(database);
            Console.WriteLine($"Dependency graph built with {allDependencies.Count} objects");

            Console.WriteLine("Performing topological sort...");
            var orderedObjects = TopologicalSort(allDependencies);
            Console.WriteLine($"Topological sort complete: {orderedObjects.Count} objects ordered");

            Console.WriteLine("Building object-to-file map...");
            // Map object identifiers to their script file paths
            var objectFileMap = BuildObjectFileMap(database, outputDirectory);
            Console.WriteLine($"Object file map built with {objectFileMap.Count} mappings");

            // Group objects by type for organized output
            var schemaObjects = new List<string>();
            var userObjects = new List<string>();
            var tableObjects = new List<string>();
            var viewObjects = new List<string>();
            var functionObjects = new List<string>();
            var procedureObjects = new List<string>();
            var synonymObjects = new List<string>();

            Console.WriteLine("Grouping objects by type...");
            int mappedCount = 0;
            int unmappedCount = 0;

            foreach (var objId in orderedObjects)
            {
                if (!objectFileMap.ContainsKey(objId))
                {
                    Console.WriteLine($"Warning: No file found for object {objId}");
                    unmappedCount++;
                    continue;
                }

                mappedCount++;
                var filePath = objectFileMap[objId];

                if (filePath.Contains("Schema\\"))
                    schemaObjects.Add(filePath);
                else if (filePath.Contains("User\\"))
                    userObjects.Add(filePath);
                else if (filePath.Contains("Table\\"))
                    tableObjects.Add(filePath);
                else if (filePath.Contains("View\\"))
                    viewObjects.Add(filePath);
                else if (filePath.Contains("UserDefinedFunction\\"))
                    functionObjects.Add(filePath);
                else if (filePath.Contains("StoredProcedure\\"))
                    procedureObjects.Add(filePath);
                else if (filePath.Contains("Synonym\\"))
                    synonymObjects.Add(filePath);
            }

            Console.WriteLine($"Grouped {mappedCount} objects into categories");
            if (unmappedCount > 0)
            {
                Console.WriteLine($"WARNING: {unmappedCount} objects could not be mapped to files");
            }
            Console.WriteLine($"  Schemas: {schemaObjects.Count}");
            Console.WriteLine($"  Users: {userObjects.Count}");
            Console.WriteLine($"  Tables: {tableObjects.Count}");
            Console.WriteLine($"  Views: {viewObjects.Count}");
            Console.WriteLine($"  Functions: {functionObjects.Count}");
            Console.WriteLine($"  Procedures: {procedureObjects.Count}");
            Console.WriteLine($"  Synonyms: {synonymObjects.Count}");

            // Output in execution order
            if (schemaObjects.Any())
            {
                executionOrder.Add("-- STEP 1: Create Schemas");
                executionOrder.AddRange(schemaObjects);
                executionOrder.Add("");
            }

            if (userObjects.Any())
            {
                executionOrder.Add("-- STEP 2: Create Users");
                executionOrder.AddRange(userObjects);
                executionOrder.Add("");
            }

            if (tableObjects.Any())
            {
                executionOrder.Add("-- STEP 3: Create Tables (in dependency order)");
                executionOrder.Add("-- Tables are ordered so referenced tables are created before referencing tables");
                executionOrder.AddRange(tableObjects);
                executionOrder.Add("");
            }

            if (viewObjects.Any())
            {
                executionOrder.Add("-- STEP 4: Create Views (in dependency order)");
                executionOrder.Add("-- Views are ordered so referenced views are created before dependent views");
                executionOrder.AddRange(viewObjects);
                executionOrder.Add("");
            }

            if (functionObjects.Any())
            {
                executionOrder.Add("-- STEP 5: Create Functions (in dependency order)");
                executionOrder.Add("-- Functions are ordered so referenced functions are created before dependent functions");
                executionOrder.AddRange(functionObjects);
                executionOrder.Add("");
            }

            if (procedureObjects.Any())
            {
                executionOrder.Add("-- STEP 6: Create Stored Procedures (in dependency order)");
                executionOrder.Add("-- Procedures are ordered so referenced procedures are created before dependent procedures");
                executionOrder.AddRange(procedureObjects);
                executionOrder.Add("");
            }

            if (synonymObjects.Any())
            {
                executionOrder.Add("-- STEP 7: Create Synonyms");
                executionOrder.AddRange(synonymObjects);
                executionOrder.Add("");
            }

            // Data insertion follows table order
            var dataObjects = new List<string>();
            var dataDir = Path.Combine(outputDirectory, "Table\\Data");
            if (Directory.Exists(dataDir))
            {
                foreach (var objId in orderedObjects)
                {
                    if (!objId.StartsWith("TABLE:")) continue;

                    var tableName = objId.Substring(6); // Remove "TABLE:" prefix
                    var fileName = $"{tableName}.SQL";
                    var filePath = Path.Combine(dataDir, fileName);

                    if (File.Exists(filePath))
                    {
                        dataObjects.Add($"Table\\Data\\{fileName}");
                    }
                }

                if (dataObjects.Any())
                {
                    executionOrder.Add("-- STEP 8: Insert Data (in dependency order)");
                    executionOrder.Add("-- Data insertion follows same order as table creation to respect foreign keys");
                    executionOrder.AddRange(dataObjects);
                }
            }

            // Generate PowerShell script with correct order
            GenerateOrderedPowerShellScript(outputDirectory, schemaObjects, userObjects, tableObjects,
                viewObjects, functionObjects, procedureObjects, synonymObjects, dataObjects);

            var orderFile = Path.Combine(outputDirectory, "ExecutionOrder.txt");
            File.WriteAllLines(orderFile, executionOrder);

            Console.WriteLine($"Execution order file generated: {orderFile}");
        }

        /// <summary>
        /// Gets dependencies for a database object using sys.sql_expression_dependencies
        /// </summary>
        private static List<string> GetObjectDependencies(Database database, string schema, string objectName, string currentObjectId)
        {
            var dependencies = new List<string>();

            try
            {
                var sql = $@"
                    SELECT 
                        OBJECT_SCHEMA_NAME(referenced_id) AS RefSchema,
                        OBJECT_NAME(referenced_id) AS RefObject,
                        o.type_desc AS RefType
                    FROM sys.sql_expression_dependencies d
                    LEFT JOIN sys.objects o ON d.referenced_id = o.object_id
                    WHERE referencing_id = OBJECT_ID('[{schema}].[{objectName}]')
                        AND referenced_id IS NOT NULL";

                var result = database.ExecuteWithResults(sql);

                if (result.Tables.Count > 0)
                {
                    foreach (System.Data.DataRow row in result.Tables[0].Rows)
                    {
                        var refSchema = row["RefSchema"]?.ToString();
                        var refObject = row["RefObject"]?.ToString();
                        var refType = row["RefType"]?.ToString();

                        if (!string.IsNullOrEmpty(refSchema) && !string.IsNullOrEmpty(refObject))
                        {
                            string depId = null;

                            if (refType?.Contains("FUNCTION") == true)
                                depId = $"FUNCTION:{refSchema}.{refObject}";
                            else if (refType?.Contains("TABLE") == true)
                                depId = $"TABLE:{refSchema}.{refObject}";
                            else if (refType?.Contains("VIEW") == true)
                                depId = $"VIEW:{refSchema}.{refObject}";
                            else if (refType?.Contains("PROCEDURE") == true)
                                depId = $"PROCEDURE:{refSchema}.{refObject}";

                            if (depId != null && depId != currentObjectId)
                            {
                                dependencies.Add(depId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not determine dependencies for {currentObjectId}: {ex.Message}");
            }

            return dependencies;
        }

        /// <summary>
        /// Builds a comprehensive dependency graph for all database objects
        /// </summary>
        private static Dictionary<string, List<string>> BuildCompleteDependencyGraph(Database database)
        {
            var dependencies = new Dictionary<string, List<string>>();

            // Add schemas first (they have no dependencies except on each other, which is rare)
            // Note: Schema ownership dependencies are uncommon. If schemas in your database
            // have dependencies (e.g., one schema authorized to another), you may need to
            // query sys.database_principals and sys.schemas for ownership chains.
            foreach (Schema schema in database.Schemas)
            {
                if (Util.IsSystemObjectGeneric(schema)) continue;
                var objectName = schema.ToString().Replace("[", "").Replace("]", "");
                var schemaId = $"SCHEMA:{objectName}";
                dependencies[schemaId] = new List<string>();
            }

            // Add users (they depend on schemas)
            foreach (User user in database.Users)
            {
                if (Util.IsSystemObjectGeneric(user)) continue;
                var objectName = user.ToString().Replace("[", "").Replace("]", "");
                var userId = $"USER:{objectName}";
                dependencies[userId] = new List<string>();

                // Users depend on their default schema
                if (!string.IsNullOrEmpty(user.DefaultSchema))
                {
                    dependencies[userId].Add($"SCHEMA:{user.DefaultSchema}");
                }
            }

            // Add tables with foreign key dependencies
            foreach (Table table in database.Tables)
            {
                if (table.IsSystemObject) continue;

                var objectName = table.ToString().Replace("[", "").Replace("]", "");
                var tableId = $"TABLE:{objectName}";
                dependencies[tableId] = new List<string>();

                // Depend on schema
                dependencies[tableId].Add($"SCHEMA:{table.Schema}");

                // Check each foreign key to find dependencies
                foreach (ForeignKey fk in table.ForeignKeys)
                {
                    // Construct referenced table ID in same format as we create table IDs
                    // Use schema.table format to match ToString() output
                    var referencedTableName = $"{fk.ReferencedTableSchema}.{fk.ReferencedTable}";
                    var referencedTable = $"TABLE:{referencedTableName}";

                    // Don't add self-references
                    if (referencedTable != tableId)
                    {
                        dependencies[tableId].Add(referencedTable);
                    }
                }
            }

            // Add views with their dependencies
            foreach (View view in database.Views)
            {
                if (view.IsSystemObject) continue;

                var objectName = view.ToString().Replace("[", "").Replace("]", "");
                var viewId = $"VIEW:{objectName}";
                dependencies[viewId] = new List<string>();

                // Depend on schema
                dependencies[viewId].Add($"SCHEMA:{view.Schema}");

                // Get view dependencies from sys.sql_expression_dependencies
                var viewDeps = GetObjectDependencies(database, view.Schema, view.Name, viewId);
                dependencies[viewId].AddRange(viewDeps);
            }

            // Add user-defined functions with their dependencies
            foreach (UserDefinedFunction function in database.UserDefinedFunctions)
            {
                if (function.IsSystemObject) continue;

                var objectName = function.ToString().Replace("[", "").Replace("]", "");
                var functionId = $"FUNCTION:{objectName}";
                dependencies[functionId] = new List<string>();

                // Depend on schema
                dependencies[functionId].Add($"SCHEMA:{function.Schema}");

                // Get function dependencies from sys.sql_expression_dependencies
                var funcDeps = GetObjectDependencies(database, function.Schema, function.Name, functionId);
                dependencies[functionId].AddRange(funcDeps);
            }

            // Add stored procedures with their dependencies
            foreach (StoredProcedure proc in database.StoredProcedures)
            {
                if (proc.IsSystemObject) continue;

                var objectName = proc.ToString().Replace("[", "").Replace("]", "");
                var procId = $"PROCEDURE:{objectName}";
                dependencies[procId] = new List<string>();

                // Depend on schema
                dependencies[procId].Add($"SCHEMA:{proc.Schema}");

                // Get procedure dependencies from sys.sql_expression_dependencies
                var procDeps = GetObjectDependencies(database, proc.Schema, proc.Name, procId);
                dependencies[procId].AddRange(procDeps);
            }

            // Add synonyms (they depend on their target objects)
            foreach (Synonym synonym in database.Synonyms)
            {
                if (Util.IsSystemObjectGeneric(synonym)) continue;

                var objectName = synonym.ToString().Replace("[", "").Replace("]", "");
                var synonymId = $"SYNONYM:{objectName}";
                dependencies[synonymId] = new List<string>();

                // Depend on schema
                dependencies[synonymId].Add($"SCHEMA:{synonym.Schema}");

                // Get the base object the synonym points to
                try
                {
                    var baseObject = synonym.BaseObject;
                    var baseSchema = synonym.BaseSchema;
                    var baseServer = synonym.BaseServer;
                    var baseDatabase = synonym.BaseDatabase;

                    // Only track dependencies on objects in the same database
                    if (string.IsNullOrEmpty(baseServer) && string.IsNullOrEmpty(baseDatabase))
                    {
                        if (!string.IsNullOrEmpty(baseSchema) && !string.IsNullOrEmpty(baseObject))
                        {
                            // Try to determine what type of object this synonym references
                            var sql = $@"
                                SELECT o.type_desc AS ObjectType
                                FROM sys.objects o
                                WHERE o.name = '{baseObject}' 
                                    AND OBJECT_SCHEMA_NAME(o.object_id) = '{baseSchema}'";

                            var result = database.ExecuteWithResults(sql);

                            if (result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
                            {
                                var objectType = result.Tables[0].Rows[0]["ObjectType"]?.ToString();
                                string depId = null;

                                if (objectType?.Contains("TABLE") == true)
                                    depId = $"TABLE:{baseSchema}.{baseObject}";
                                else if (objectType?.Contains("VIEW") == true)
                                    depId = $"VIEW:{baseSchema}.{baseObject}";
                                else if (objectType?.Contains("FUNCTION") == true)
                                    depId = $"FUNCTION:{baseSchema}.{baseObject}";
                                else if (objectType?.Contains("PROCEDURE") == true)
                                    depId = $"PROCEDURE:{baseSchema}.{baseObject}";

                                if (depId != null)
                                {
                                    dependencies[synonymId].Add(depId);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not determine base object for synonym {synonym.Schema}.{synonym.Name}: {ex.Message}");
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Maps object identifiers to their script file paths
        /// </summary>
        private static Dictionary<string, string> BuildObjectFileMap(Database database, string outputDirectory)
        {
            var fileMap = new Dictionary<string, string>();

            // Map schemas
            var schemaDir = Path.Combine(outputDirectory, "Schema");
            foreach (Schema schema in database.Schemas)
            {
                if (Util.IsSystemObjectGeneric(schema)) continue;
                var objectName = schema.ToString().Replace("[", "").Replace("]", "");
                var schemaId = $"SCHEMA:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(schemaDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[schemaId] = $"Schema\\{fileName}";
                }
            }

            // Map users
            var userDir = Path.Combine(outputDirectory, "User");
            foreach (User user in database.Users)
            {
                if (Util.IsSystemObjectGeneric(user)) continue;
                var objectName = user.ToString().Replace("[", "").Replace("]", "");
                var userId = $"USER:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(userDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[userId] = $"User\\{fileName}";
                }
            }

            // Map tables
            var tableDir = Path.Combine(outputDirectory, "Table");
            foreach (Table table in database.Tables)
            {
                if (table.IsSystemObject) continue;
                var objectName = table.ToString().Replace("[", "").Replace("]", "");
                var tableId = $"TABLE:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(tableDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[tableId] = $"Table\\{fileName}";
                }
            }

            // Map views
            var viewDir = Path.Combine(outputDirectory, "View");
            foreach (View view in database.Views)
            {
                if (view.IsSystemObject) continue;
                var objectName = view.ToString().Replace("[", "").Replace("]", "");
                var viewId = $"VIEW:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(viewDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[viewId] = $"View\\{fileName}";
                }
            }

            // Map functions
            var functionDir = Path.Combine(outputDirectory, "UserDefinedFunction");
            foreach (UserDefinedFunction function in database.UserDefinedFunctions)
            {
                if (function.IsSystemObject) continue;
                var objectName = function.ToString().Replace("[", "").Replace("]", "");
                var functionId = $"FUNCTION:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(functionDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[functionId] = $"UserDefinedFunction\\{fileName}";
                }
            }

            // Map stored procedures
            var procDir = Path.Combine(outputDirectory, "StoredProcedure");
            foreach (StoredProcedure proc in database.StoredProcedures)
            {
                if (proc.IsSystemObject) continue;
                var objectName = proc.ToString().Replace("[", "").Replace("]", "");
                var procId = $"PROCEDURE:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(procDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[procId] = $"StoredProcedure\\{fileName}";
                }
            }

            // Map synonyms
            var synonymDir = Path.Combine(outputDirectory, "Synonym");
            foreach (Synonym synonym in database.Synonyms)
            {
                if (Util.IsSystemObjectGeneric(synonym)) continue;
                var objectName = synonym.ToString().Replace("[", "").Replace("]", "");
                var synonymId = $"SYNONYM:{objectName}";
                var fileName = $"{objectName}.SQL";
                var filePath = Path.Combine(synonymDir, fileName);
                if (File.Exists(filePath))
                {
                    fileMap[synonymId] = $"Synonym\\{fileName}";
                }
            }

            Console.WriteLine($"BuildObjectFileMap: Mapped {fileMap.Count} objects to files");
            return fileMap;
        }

        /// <summary>
        /// Performs topological sort to order objects by dependencies
        /// </summary>
        private static List<string> TopologicalSort(Dictionary<string, List<string>> dependencies)
        {
            var sorted = new List<string>();
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();
            var circularDependencies = new List<string>();

            void Visit(string node)
            {
                // Handle circular dependencies by breaking the cycle
                if (recursionStack.Contains(node))
                {
                    circularDependencies.Add(node);
                    Console.WriteLine($"WARNING: Circular dependency detected involving {node}");
                    Console.WriteLine($"         This may cause script execution to fail. Manual intervention may be required.");
                    return;
                }

                if (visited.Contains(node))
                    return;

                recursionStack.Add(node);

                // Visit all dependencies first
                if (dependencies.ContainsKey(node))
                {
                    foreach (var dependency in dependencies[node])
                    {
                        Visit(dependency);
                    }
                }

                recursionStack.Remove(node);
                visited.Add(node);
                sorted.Add(node);
            }

            // Visit all nodes
            foreach (var node in dependencies.Keys)
            {
                Visit(node);
            }

            // Report circular dependency summary
            if (circularDependencies.Any())
            {
                Console.WriteLine("");
                Console.WriteLine("========================================");
                Console.WriteLine("CIRCULAR DEPENDENCY WARNING");
                Console.WriteLine("========================================");
                Console.WriteLine($"Detected {circularDependencies.Count} object(s) involved in circular dependencies:");
                foreach (var obj in circularDependencies.Distinct())
                {
                    Console.WriteLine($"  - {obj}");
                }
                Console.WriteLine("These objects may need to be created manually or with ALTER statements after initial creation.");
                Console.WriteLine("========================================");
                Console.WriteLine("");
            }

            return sorted;
        }

        /// <summary>
        /// Generates PowerShell execution script for all object types in dependency order
        /// </summary>
        private static void GenerateOrderedPowerShellScript(string outputDirectory,
            List<string> schemaObjects, List<string> userObjects, List<string> tableObjects,
            List<string> viewObjects, List<string> functionObjects, List<string> procedureObjects,
            List<string> synonymObjects, List<string> dataObjects)
        {
            var psScript = new List<string>();

            psScript.Add("# Database Script Executor - Dependency Aware");
            psScript.Add("# Executes all database object scripts in correct dependency order");
            psScript.Add("# Uses temporary database with rename-on-success strategy to avoid partial failures");
            psScript.Add("param(");
            psScript.Add("    [Parameter(Mandatory=$true)]");
            psScript.Add("    [string]$ServerInstance,");
            psScript.Add("    [Parameter(Mandatory=$true)]");
            psScript.Add("    [string]$DatabaseName,");
            psScript.Add("    [switch]$Force  # Drop existing database if it exists");
            psScript.Add(")");
            psScript.Add("");
            psScript.Add("$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path");
            psScript.Add("$ErrorActionPreference = 'Stop'");
            psScript.Add("$executedCount = 0");
            psScript.Add("$totalScripts = 0");
            psScript.Add("$tempDbName = \"${DatabaseName}_TEMP_\" + (Get-Date -Format 'yyyyMMddHHmmss')");
            psScript.Add("");
            psScript.Add("Write-Host '========================================'");
            psScript.Add("Write-Host 'Database Script Executor' -ForegroundColor Green");
            psScript.Add("Write-Host '========================================'");
            psScript.Add("Write-Host \"Target Database: $DatabaseName\"");
            psScript.Add("Write-Host \"Temporary Database: $tempDbName\"");
            psScript.Add("Write-Host \"Server: $ServerInstance\"");
            psScript.Add("Write-Host '========================================'");
            psScript.Add("");

            // Check for existing database
            psScript.Add("# Check if target database already exists");
            psScript.Add("$existingDb = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database master -Query \"SELECT name FROM sys.databases WHERE name = '$DatabaseName'\" -ErrorAction SilentlyContinue");
            psScript.Add("if ($existingDb) {");
            psScript.Add("    if ($Force) {");
            psScript.Add("        Write-Host \"WARNING: Database '$DatabaseName' exists and will be replaced.\" -ForegroundColor Yellow");
            psScript.Add("        Write-Host \"Dropping existing database...\" -ForegroundColor Yellow");
            psScript.Add("        Invoke-Sqlcmd -ServerInstance $ServerInstance -Database master -Query \"ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName];\"");
            psScript.Add("    } else {");
            psScript.Add("        Write-Host \"ERROR: Database '$DatabaseName' already exists. Use -Force to replace it.\" -ForegroundColor Red");
            psScript.Add("        exit 1");
            psScript.Add("    }");
            psScript.Add("}");
            psScript.Add("");

            // Create temporary database
            psScript.Add("# Create temporary database");
            psScript.Add("Write-Host \"Creating temporary database: $tempDbName\" -ForegroundColor Cyan");
            psScript.Add("Invoke-Sqlcmd -ServerInstance $ServerInstance -Database master -Query \"CREATE DATABASE [$tempDbName]\"");
            psScript.Add("");

            psScript.Add("try {");
            psScript.Add("");
            psScript.Add("    # Count total scripts to execute");

            int totalScripts = schemaObjects.Count + userObjects.Count + tableObjects.Count +
                              viewObjects.Count + functionObjects.Count + procedureObjects.Count +
                              synonymObjects.Count + dataObjects.Count;

            psScript.Add($"    $totalScripts = {totalScripts}");
            psScript.Add("    Write-Host \"Total scripts to execute: $totalScripts\" -ForegroundColor Cyan");
            psScript.Add("");

            // Function to execute a script file
            psScript.Add("    function Execute-SqlScript {");
            psScript.Add("        param([string]$RelativePath)");
            psScript.Add("        $file = Join-Path $scriptPath $RelativePath");
            psScript.Add("        if (Test-Path $file) {");
            psScript.Add("            $percentage = [math]::Round(($script:executedCount / $totalScripts) * 100)");
            psScript.Add("            Write-Host \"[$percentage%] ($($script:executedCount + 1)/$totalScripts) Executing: $RelativePath\" -ForegroundColor Cyan");
            psScript.Add("            try {");
            psScript.Add("                Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $tempDbName -InputFile $file -ErrorAction Stop");
            psScript.Add("                $script:executedCount++");
            psScript.Add("            } catch {");
            psScript.Add("                Write-Host \"ERROR executing $RelativePath : $_\" -ForegroundColor Red");
            psScript.Add("                throw");
            psScript.Add("            }");
            psScript.Add("        } else {");
            psScript.Add("            Write-Host \"WARNING: File not found: $file\" -ForegroundColor Yellow");
            psScript.Add("        }");
            psScript.Add("    }");
            psScript.Add("");

            // Execute schemas
            if (schemaObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 1: Creating Schemas' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in schemaObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute users
            if (userObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 2: Creating Users' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in userObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute tables
            if (tableObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 3: Creating Tables (in dependency order)' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in tableObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute views
            if (viewObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 4: Creating Views (in dependency order)' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in viewObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute functions
            if (functionObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 5: Creating Functions (in dependency order)' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in functionObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute stored procedures
            if (procedureObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 6: Creating Stored Procedures (in dependency order)' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in procedureObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute synonyms
            if (synonymObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 7: Creating Synonyms' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in synonymObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Execute data inserts
            if (dataObjects.Any())
            {
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                psScript.Add("    Write-Host 'STEP 8: Inserting Data (in dependency order)' -ForegroundColor Green");
                psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
                foreach (var obj in dataObjects)
                {
                    psScript.Add($"    Execute-SqlScript '{obj}'");
                }
                psScript.Add("");
            }

            // Success - rename database
            psScript.Add("    # All scripts executed successfully - rename temp database to final name");
            psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
            psScript.Add("    Write-Host 'All scripts executed successfully!' -ForegroundColor Green");
            psScript.Add("    Write-Host \"Total scripts executed: $executedCount\" -ForegroundColor Green");
            psScript.Add("    Write-Host '========================================' -ForegroundColor Green");
            psScript.Add("    Write-Host \"Renaming temporary database to final name...\" -ForegroundColor Cyan");
            psScript.Add("    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database master -Query \"ALTER DATABASE [$tempDbName] MODIFY NAME = [$DatabaseName]\"");
            psScript.Add("    Write-Host \"Database '$DatabaseName' created successfully!\" -ForegroundColor Green");
            psScript.Add("");
            psScript.Add("} catch {");
            psScript.Add("    # Failure - drop temp database");
            psScript.Add("    Write-Host '========================================' -ForegroundColor Red");
            psScript.Add("    Write-Host 'ERROR: Script execution failed!' -ForegroundColor Red");
            psScript.Add("    Write-Host $_.Exception.Message -ForegroundColor Red");
            psScript.Add("    Write-Host '========================================' -ForegroundColor Red");
            psScript.Add("    Write-Host \"Cleaning up temporary database: $tempDbName\" -ForegroundColor Yellow");
            psScript.Add("    try {");
            psScript.Add("        Invoke-Sqlcmd -ServerInstance $ServerInstance -Database master -Query \"ALTER DATABASE [$tempDbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$tempDbName];\" -ErrorAction SilentlyContinue");
            psScript.Add("    } catch {");
            psScript.Add("        Write-Host \"WARNING: Could not drop temporary database. You may need to drop it manually.\" -ForegroundColor Yellow");
            psScript.Add("    }");
            psScript.Add("    exit 1");
            psScript.Add("}");

            var psFile = Path.Combine(outputDirectory, "ExecuteScripts.ps1");
            File.WriteAllLines(psFile, psScript);
            Console.WriteLine($"PowerShell execution script generated: {psFile}");
        }
    }
}