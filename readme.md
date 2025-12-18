# MSSQL Script Backup

I got fed up with databases either not being version controlled at all, or trying to monitor how
a database evolved over a project's life from migration scripts. So I built this. 

Definintely only beta at the moment, use at your own risk and test in your environment before
depending on it.

## Features

- **Configuration-driven**: Define backup jobs in simple JSON files
- **Selective data backup**: Choose which databases include data vs. objects-only
- **Multi-database support**: Backup multiple databases in a single run
- **Timestamped output**: Each backup run creates a timestamped directory

## Prerequisites

- .NET 8 or higher.
- SQL Server 
- Appropriate permissions to read database schemas and data as your logged in user

## Configuration

Create a JSON configuration file defining your backup job:
```json
{
  "serverName": "MyServer",
  "basePath": "C:\\Backups\\MyProject",
  "databases": [
    {
      "name": "MyDatabase",
      "includeData": true
    },
    {
      "name": "MyDatabase_Lookup",
      "includeData": false
    },
    {
      "name": "MyDatabase_Auth",
      "includeData": false
    }
  ]
}
```

### Configuration Options

- **serverName** (required): SQL Server instance name
  - Examples: `(local)`, `(local)\\SQLExpress`, `SERVER\\INSTANCE`, `server.domain.com`
- **basePath** (required): Root directory for backup files
  - A timestamped subdirectory will be created here for each backup run, to prevent previous
    backups accidentally being overwritten or combined with the current backup set
- **databases** (required): Array of database configurations
  - **name** (required): Database name
  - **includeData** (optional, default: true): Whether to include table data
    - `true`: Full backup with schema and data
    - `false`: Schema-only backup (structure, stored procedures, views, etc.)

## Usage

Run the tool with a configuration file:
```bash
DBScriptBackup.exe backup-config.json
```

### Example Output
```
Loading configuration from: backup-config.json
Server: (local)\SQLExpress
Base Path: C:\repo\MyProject
Databases:
  - MyProject (Data: Yes)
  - MyProject_App (Data: No)

=== Processing Database: MyProject (Include Data: True) ===
[Database scripting details...]

=== Processing Database: MyProject_App (Include Data: False) ===
[Database scripting details...]

All databases have been scripted successfully!
```

### Output Structure

Backups are organized in timestamped directories:
```
C:\repo\MyProject\
├── DB-20241218143022\
│   ├── MyProject\
│   │   ├── Tables\
│   │   ├── StoredProcedures\
│   │   ├── Views\
│   │   └── Data\
│   ├── MyProject_App\
│   └── MyProject_Auth\
└── DB-20241218150515\
    └── ...
```

## Use Cases

### Version Control Integration
Commit backups to Git for tracking database changes:
```json
{
  "databases": [
    {
      "name": "MyDatabase",
      "includeData": false
    }
  ]
}
```

### Full Backup for Development
Include data for local development environments:
```json
{
  "databases": [
    {
      "name": "MyDatabase",
      "includeData": true
    }
  ]
}
```

### Mixed Strategy
Common pattern for multi-database applications:
- Back up important configuration data for easy system restore and change detection
- Skip data for installation-specific databases e.g. logging, client data
```json
{
  "databases": [
    {
      "name": "MyApp_Transactions",
      "includeData": true
    },
    {
      "name": "MyApp_Config",
      "includeData": true
    },
    {
      "name": "MyApp_Countries",
      "includeData": false
    }
  ]
}
```

## Automation

### Scheduled Backups (Windows Task Scheduler)

Create a batch file (`backup.bat`):
```batch
@echo off
cd C:\path\to\DBScriptBackup
DBScriptBackup.exe production-backup.json
```

Schedule it to run daily/weekly using Task Scheduler.

### Pre-commit Hook

Add to `.git/hooks/pre-commit`:
```bash
#!/bin/bash
DBScriptBackup.exe schema-only-config.json
git add DB-*/
```

## Troubleshooting

### "Configuration file not found"
- Verify the path to your JSON file is correct
- Use absolute paths or ensure you're running from the correct directory

### "Failed to connect to database"
- Check the `serverName` in your configuration
- Verify SQL Server is running
- Ensure the session user has appropriate permissions 
- Test connection with SQL Server Management Studio

### "Access denied" errors
- Run as administrator if backing up system databases
- Verify file system permissions on the `basePath` directory

## Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Submit a pull request

## License

MIT

## Author

Greg McAllister-Webb
